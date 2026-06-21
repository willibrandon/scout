namespace Scout;

internal sealed class RegexLineContainsEngine
{
    private readonly byte[] literal;
    private readonly MemmemFinder finder;
    private readonly bool dotMatchesNewline;
    private readonly byte lineTerminator;

    private RegexLineContainsEngine(byte[] literal, bool dotMatchesNewline, byte lineTerminator)
    {
        this.literal = literal;
        finder = new MemmemFinder(literal);
        this.dotMatchesNewline = dotMatchesNewline;
        this.lineTerminator = lineTerminator;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexLineContainsEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive || options.Crlf)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode sequence ||
            !TryGetLineContainsLiteral(sequence, out byte[] literal))
        {
            return false;
        }

        engine = new RegexLineContainsEngine(literal, options.DotMatchesNewline, options.LineTerminator);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt <= haystack.Length)
        {
            int lineEnd = FindLineEnd(haystack, searchAt);
            if (finder.Find(haystack[searchAt..lineEnd]) >= 0)
            {
                return new RegexMatch(searchAt, lineEnd - searchAt);
            }

            if (lineEnd >= haystack.Length)
            {
                return null;
            }

            searchAt = lineEnd + 1;
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int lineEnd = FindLineEnd(haystack, start);
        return finder.Find(haystack[start..lineEnd]) >= 0
            ? new RegexMatch(start, lineEnd - start)
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

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt <= haystack.Length)
        {
            int lineEnd = FindLineEnd(haystack, searchAt);
            if (finder.Find(haystack[searchAt..lineEnd]) >= 0)
            {
                total += sumSpans ? lineEnd - searchAt : 1;
            }

            if (lineEnd >= haystack.Length)
            {
                return total;
            }

            searchAt = lineEnd + 1;
        }

        return total;
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

    private static bool TryGetLineContainsLiteral(RegexSequenceNode sequence, out byte[] literal)
    {
        literal = [];
        var bytes = new List<byte>();
        bool sawLeadingDotStar = false;
        bool sawLiteral = false;
        bool sawTrailingDotStar = false;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode node = UnwrapTransparentGroups(sequence.Nodes[index]);
            if (IsGreedyDotStar(node))
            {
                if (sawLiteral)
                {
                    sawTrailingDotStar = true;
                }
                else
                {
                    sawLeadingDotStar = true;
                }

                continue;
            }

            if (node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
            {
                sawLiteral = true;
                bytes.AddRange(atom.Value.ToArray());
                continue;
            }

            return false;
        }

        if (!sawLeadingDotStar || !sawLiteral || !sawTrailingDotStar || bytes.Count == 0)
        {
            return false;
        }

        literal = bytes.ToArray();
        return true;
    }

    private static bool IsGreedyDotStar(RegexSyntaxNode node)
    {
        return node is RegexRepetitionNode
        {
            Child.Kind: RegexSyntaxKind.Dot,
            Minimum: 0,
            Maximum: null,
            Lazy: false,
        };
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
