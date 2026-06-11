namespace Scout;

internal sealed class RegexDotStarClassFallbackEngine
{
    private readonly byte[] positiveClass;
    private readonly bool caseInsensitive;
    private readonly bool multiLine;
    private readonly bool dotMatchesNewline;
    private readonly bool crlf;
    private readonly byte lineTerminator;

    private RegexDotStarClassFallbackEngine(byte[] positiveClass, RegexCompileOptions options)
    {
        this.positiveClass = positiveClass;
        caseInsensitive = options.CaseInsensitive;
        multiLine = options.MultiLine;
        dotMatchesNewline = options.DotMatchesNewline;
        crlf = options.Crlf;
        lineTerminator = options.LineTerminator;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexDotStarClassFallbackEngine? engine)
    {
        engine = null;
        if (options.Crlf)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexAlternationNode { Alternatives.Count: 2 } alternation ||
            !TryGetDotStarNegatedClass(alternation.Alternatives[0], out byte[] positiveClass) ||
            !IsPositiveClass(alternation.Alternatives[1], positiveClass))
        {
            return false;
        }

        engine = new RegexDotStarClassFallbackEngine(positiveClass, options);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt < haystack.Length)
        {
            int lineEnd = FindLineEnd(haystack, searchAt);
            if (TryMatchInLine(haystack, searchAt, lineEnd, out RegexMatch match))
            {
                return match;
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
        return TryMatchInLine(haystack, start, lineEnd, out RegexMatch match) ? match : null;
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
        while (searchAt < haystack.Length)
        {
            int lineEnd = FindLineEnd(haystack, searchAt);
            int lastNonClass = FindLastNonClass(haystack, searchAt, lineEnd);
            if (lastNonClass >= searchAt)
            {
                total += sumSpans ? lastNonClass - searchAt + 1 : 1;
                searchAt = lastNonClass + 1;
            }

            if (searchAt < lineEnd)
            {
                int classRunLength = lineEnd - searchAt;
                total += sumSpans ? classRunLength : classRunLength;
                searchAt = lineEnd;
            }

            if (lineEnd >= haystack.Length)
            {
                return total;
            }

            searchAt = lineEnd + 1;
        }

        return total;
    }

    private bool TryMatchInLine(ReadOnlySpan<byte> haystack, int start, int lineEnd, out RegexMatch match)
    {
        for (int index = lineEnd - 1; index >= start; index--)
        {
            if (!ClassMatches(haystack[index]))
            {
                match = new RegexMatch(start, index - start + 1);
                return true;
            }
        }

        if (start < lineEnd && ClassMatches(haystack[start]))
        {
            match = new RegexMatch(start, 1);
            return true;
        }

        match = default;
        return false;
    }

    private int FindLastNonClass(ReadOnlySpan<byte> haystack, int start, int lineEnd)
    {
        for (int index = lineEnd - 1; index >= start; index--)
        {
            if (!ClassMatches(haystack[index]))
            {
                return index;
            }
        }

        return -1;
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

    private bool ClassMatches(byte value)
    {
        return RegexByteClass.AtomMatches(
            value,
            RegexSyntaxKind.CharacterClass,
            positiveClass,
            caseInsensitive,
            multiLine,
            dotMatchesNewline,
            crlf,
            lineTerminator);
    }

    private static bool TryGetDotStarNegatedClass(RegexSyntaxNode node, out byte[] positiveClass)
    {
        positiveClass = [];
        node = UnwrapTransparentGroups(node);
        if (node is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            !IsGreedyDotStar(UnwrapTransparentGroups(sequence.Nodes[0])) ||
            UnwrapTransparentGroups(sequence.Nodes[1]) is not RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom ||
            atom.Value.Length < 2 ||
            atom.Value.Span[0] != (byte)'^')
        {
            return false;
        }

        positiveClass = atom.Value[1..].ToArray();
        return positiveClass.Length > 0;
    }

    private static bool IsPositiveClass(RegexSyntaxNode node, ReadOnlySpan<byte> positiveClass)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            atom.Value.Span.SequenceEqual(positiveClass);
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
