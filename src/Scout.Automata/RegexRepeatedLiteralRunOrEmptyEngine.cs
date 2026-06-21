namespace Scout;

internal sealed class RegexRepeatedLiteralRunOrEmptyEngine
{
    private const int MaxMinimumRunLength = 4096;

    private readonly byte literal;
    private readonly int minimumRunLength;

    private RegexRepeatedLiteralRunOrEmptyEngine(byte literal, int minimumRunLength)
    {
        this.literal = literal;
        this.minimumRunLength = minimumRunLength;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexRepeatedLiteralRunOrEmptyEngine? engine)
    {
        engine = null;
        if (captureCount != 0 ||
            options.CaseInsensitive ||
            options.SwapGreed ||
            options.Utf8)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexAlternationNode { Alternatives.Count: 2 } alternation ||
            UnwrapTransparentGroups(alternation.Alternatives[1]).Kind != RegexSyntaxKind.Empty ||
            !TryGetRepeatedLiteralRun(alternation.Alternatives[0], out byte literal, out int minimumRunLength))
        {
            return false;
        }

        engine = new RegexRepeatedLiteralRunOrEmptyEngine(literal, minimumRunLength);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int runLength = CountLiteralRun(haystack, start);
        return runLength >= minimumRunLength
            ? new RegexMatch(start, runLength)
            : new RegexMatch(start, 0);
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        return Find(haystack, startAt);
    }

    public static bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        _ = haystack;
        return true;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        CountOrSum(haystack, startAt, sumSpans: false, out long count, out _);
        return count;
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        CountOrSum(haystack, startAt, sumSpans: true, out _, out long spanSum);
        return spanSum;
    }

    public static RegexMatch? FindEarliest(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return new RegexMatch(start, 0);
    }

    public RegexMatch? FindAllKindAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        return Find(haystack, startAt);
    }

    public IReadOnlyList<RegexMatch> FindOverlappingAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int runLength = CountLiteralRun(haystack, start);
        if (runLength >= minimumRunLength)
        {
            return [new RegexMatch(start, 0), new RegexMatch(start, runLength)];
        }

        return [new RegexMatch(start, 0)];
    }

    private void CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, out long count, out long spanSum)
    {
        count = 0;
        spanSum = 0;
        int position = Math.Clamp(startAt, 0, haystack.Length);
        while (position <= haystack.Length)
        {
            int runLength = CountLiteralRun(haystack, position);
            if (runLength >= minimumRunLength)
            {
                count++;
                if (sumSpans)
                {
                    spanSum += runLength;
                }

                position += runLength;
                continue;
            }

            if (runLength > 0)
            {
                count += runLength;
                position += runLength;
                continue;
            }

            count++;
            position++;
        }
    }

    private int CountLiteralRun(ReadOnlySpan<byte> haystack, int start)
    {
        int position = start;
        while (position < haystack.Length && haystack[position] == literal)
        {
            position++;
        }

        return position - start;
    }

    private static bool TryGetRepeatedLiteralRun(RegexSyntaxNode node, out byte literal, out int minimumRunLength)
    {
        literal = 0;
        minimumRunLength = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: { } outerMaximum,
                Lazy: false,
            } outer ||
            outerMaximum != outer.Minimum)
        {
            return false;
        }

        RegexSyntaxNode child = UnwrapTransparentGroups(outer.Child);
        if (child is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: null,
                Lazy: false,
            } inner ||
            UnwrapTransparentGroups(inner.Child) is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom ||
            atom.Value.Length != 1 ||
            outer.Minimum > MaxMinimumRunLength / inner.Minimum)
        {
            return false;
        }

        int candidateMinimum = outer.Minimum * inner.Minimum;
        if (candidateMinimum <= 0 || candidateMinimum > MaxMinimumRunLength)
        {
            return false;
        }

        literal = atom.Value.Span[0];
        minimumRunLength = candidateMinimum;
        return true;
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
