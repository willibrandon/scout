namespace Scout;

internal sealed class RegexDelimitedSpanEngine
{
    private readonly byte startByte;
    private readonly byte endByte;
    private readonly int minimumContentLength;
    private readonly bool includeStandaloneEnd;

    private RegexDelimitedSpanEngine(byte startByte, byte endByte, int minimumContentLength, bool includeStandaloneEnd)
    {
        this.startByte = startByte;
        this.endByte = endByte;
        this.minimumContentLength = minimumContentLength;
        this.includeStandaloneEnd = includeStandaloneEnd;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexDelimitedSpanEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (TryGetDelimitedSpan(root, options, out byte startByte, out byte endByte, out int minimumContentLength))
        {
            engine = new RegexDelimitedSpanEngine(startByte, endByte, minimumContentLength, includeStandaloneEnd: false);
            return true;
        }

        if (root is RegexAlternationNode { Alternatives.Count: 2 } alternation &&
            TryGetDelimitedSpan(alternation.Alternatives[0], options, out startByte, out endByte, out minimumContentLength) &&
            TryGetLiteralByte(alternation.Alternatives[1], out byte standaloneEnd) &&
            standaloneEnd == endByte &&
            startByte != endByte)
        {
            engine = new RegexDelimitedSpanEngine(startByte, endByte, minimumContentLength, includeStandaloneEnd: true);
            return true;
        }

        if (root is RegexAlternationNode { Alternatives.Count: 2 } reversed &&
            TryGetLiteralByte(reversed.Alternatives[0], out standaloneEnd) &&
            TryGetDelimitedSpan(reversed.Alternatives[1], options, out startByte, out endByte, out minimumContentLength) &&
            standaloneEnd == endByte &&
            startByte != endByte)
        {
            engine = new RegexDelimitedSpanEngine(startByte, endByte, minimumContentLength, includeStandaloneEnd: true);
            return true;
        }

        return false;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt < haystack.Length)
        {
            int found = FindNextCandidate(haystack, searchAt);
            if (found < 0)
            {
                return null;
            }

            if (includeStandaloneEnd && haystack[found] == endByte)
            {
                return new RegexMatch(found, 1);
            }

            if (TryMatchDelimitedAt(haystack, found, out int length))
            {
                return new RegexMatch(found, length);
            }

            searchAt = found + 1;
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (start >= haystack.Length)
        {
            return null;
        }

        if (includeStandaloneEnd && haystack[start] == endByte)
        {
            return new RegexMatch(start, 1);
        }

        return TryMatchDelimitedAt(haystack, start, out int length)
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

    private int FindNextCandidate(ReadOnlySpan<byte> haystack, int searchAt)
    {
        if (includeStandaloneEnd)
        {
            int offset = haystack[searchAt..].IndexOfAny(startByte, endByte);
            return offset < 0 ? -1 : searchAt + offset;
        }

        int startOffset = haystack[searchAt..].IndexOf(startByte);
        return startOffset < 0 ? -1 : searchAt + startOffset;
    }

    private bool TryMatchDelimitedAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            haystack[start] != startByte ||
            start + 1 + minimumContentLength >= haystack.Length)
        {
            return false;
        }

        int contentStart = start + 1;
        int endSearchStart = contentStart + minimumContentLength;
        for (int index = contentStart; index < endSearchStart; index++)
        {
            if (haystack[index] == endByte)
            {
                return false;
            }
        }

        if (haystack[endSearchStart] == endByte)
        {
            length = endSearchStart + 1 - start;
            return true;
        }

        int remainingStart = endSearchStart + 1;
        if (remainingStart < haystack.Length && haystack[remainingStart] == endByte)
        {
            length = remainingStart + 1 - start;
            return true;
        }

        if (remainingStart >= haystack.Length)
        {
            return false;
        }

        int endOffset = haystack[(remainingStart + 1)..].IndexOf(endByte);
        if (endOffset < 0)
        {
            return false;
        }

        int end = remainingStart + 1 + endOffset;
        length = end + 1 - start;
        return true;
    }

    private static bool TryGetDelimitedSpan(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out byte startByte,
        out byte endByte,
        out int minimumContentLength)
    {
        startByte = 0;
        endByte = 0;
        minimumContentLength = 0;
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexSequenceNode sequence ||
            sequence.Nodes.Count is not (3 or 4) ||
            !TryGetLiteralByte(sequence.Nodes[0], out startByte) ||
            !TryGetLiteralByte(sequence.Nodes[^1], out endByte))
        {
            return false;
        }

        if (sequence.Nodes.Count == 3)
        {
            return TryGetNegatedByteRepetition(sequence.Nodes[1], options, endByte);
        }

        if (TryGetNegatedByteAtom(sequence.Nodes[1], options, endByte) &&
            TryGetNegatedByteRepetition(sequence.Nodes[2], options, endByte))
        {
            minimumContentLength = 1;
            return true;
        }

        return false;
    }

    private static bool TryGetNegatedByteRepetition(RegexSyntaxNode node, RegexCompileOptions options, byte excluded)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: null,
                Lazy: false,
            } repetition &&
            TryGetNegatedByteAtom(repetition.Child, options, excluded);
    }

    private static bool TryGetNegatedByteAtom(RegexSyntaxNode node, RegexCompileOptions options, byte excluded)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexAtomNode atom ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bool expected = value != excluded;
            bool actual = RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                atom.Value.Span,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator);
            if (actual != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetLiteralByte(RegexSyntaxNode node, out byte value)
    {
        value = 0;
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Length == 1)
        {
            value = atom.Value.Span[0];
            return true;
        }

        return false;
    }

    private static RegexSyntaxNode UnwrapTransparentNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }
}
