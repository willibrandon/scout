namespace Scout;

internal sealed class RegexUriEngine
{
    private RegexUriEngine()
    {
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexUriEngine? engine)
    {
        engine = null;
        if (options.Utf8 ||
            options.UnicodeClasses ||
            options.CaseInsensitive ||
            !IsUriPattern(root))
        {
            return false;
        }

        engine = new RegexUriEngine();
        return true;
    }

    public static RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int minimumStart = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = minimumStart;
        while (searchAt < haystack.Length)
        {
            int relative = haystack[searchAt..].IndexOf("://"u8);
            if (relative < 0)
            {
                return null;
            }

            int colon = searchAt + relative;
            if (TryMatchAroundDelimiter(haystack, minimumStart, colon, out int start, out int length))
            {
                return new RegexMatch(start, length);
            }

            searchAt = colon + 1;
        }

        return null;
    }

    public static RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int colon = start;
        while (colon < haystack.Length && RegexSimpleSequenceSegment.IsAsciiWord(haystack[colon]))
        {
            colon++;
        }

        return colon + 2 < haystack.Length &&
            haystack[colon] == (byte)':' &&
            haystack[colon + 1] == (byte)'/' &&
            haystack[colon + 2] == (byte)'/' &&
            TryMatchAroundDelimiter(haystack, start, colon, out int matchStart, out int length) &&
            matchStart == start
                ? new RegexMatch(start, length)
                : null;
    }

    public static long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    public static long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    private static long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = offset;
        while (searchAt < haystack.Length)
        {
            int relative = haystack[searchAt..].IndexOf("://"u8);
            if (relative < 0)
            {
                return total;
            }

            int colon = searchAt + relative;
            if (TryMatchAroundDelimiter(haystack, offset, colon, out int start, out int length))
            {
                total += sumSpans ? length : 1;
                offset = start + length;
                searchAt = offset;
                continue;
            }

            searchAt = colon + 1;
        }

        return total;
    }

    private static bool TryMatchAroundDelimiter(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        int colon,
        out int start,
        out int length)
    {
        start = colon - 1;
        while (start >= minimumStart && RegexSimpleSequenceSegment.IsAsciiWord(haystack[start]))
        {
            start--;
        }

        start++;
        if (colon - start < 1)
        {
            length = 0;
            return false;
        }

        int bodyStart = colon + 3;
        if (bodyStart >= haystack.Length || !IsAuthorityFirstByte(haystack[bodyStart]))
        {
            length = 0;
            return false;
        }

        int bodyEnd = bodyStart + 1;
        while (bodyEnd < haystack.Length && IsBodyByte(haystack[bodyEnd]))
        {
            bodyEnd++;
        }

        if (bodyEnd - bodyStart < 2)
        {
            length = 0;
            return false;
        }

        int end = bodyEnd;
        if (end < haystack.Length && haystack[end] == (byte)'?')
        {
            end++;
            while (end < haystack.Length && !RegexSimpleSequenceSegment.IsRegexWhitespace(haystack[end]) && haystack[end] != (byte)'#')
            {
                end++;
            }
        }

        if (end < haystack.Length && haystack[end] == (byte)'#')
        {
            end++;
            while (end < haystack.Length && !RegexSimpleSequenceSegment.IsRegexWhitespace(haystack[end]))
            {
                end++;
            }
        }

        length = end - start;
        return true;
    }

    private static bool IsAuthorityFirstByte(byte value)
    {
        return !RegexSimpleSequenceSegment.IsRegexWhitespace(value) &&
            value is not (byte)'/' and not (byte)'?' and not (byte)'#';
    }

    private static bool IsBodyByte(byte value)
    {
        return !RegexSimpleSequenceSegment.IsRegexWhitespace(value) &&
            value is not (byte)'?' and not (byte)'#';
    }

    private static bool IsUriPattern(RegexSyntaxNode root)
    {
        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 8 } sequence)
        {
            return false;
        }

        return IsRepeatedClass(sequence.Nodes[0], @"\w"u8, minimum: 1, maximum: null) &&
            IsLiteral(sequence.Nodes[1], ":"u8) &&
            IsLiteral(sequence.Nodes[2], "/"u8) &&
            IsLiteral(sequence.Nodes[3], "/"u8) &&
            IsRepeatedClass(sequence.Nodes[4], @"^/\s?#"u8, minimum: 1, maximum: null) &&
            IsRepeatedClass(sequence.Nodes[5], @"^\s?#"u8, minimum: 1, maximum: null) &&
            IsOptionalQuery(sequence.Nodes[6]) &&
            IsOptionalFragment(sequence.Nodes[7]);
    }

    private static bool IsOptionalQuery(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode { Minimum: 0, Maximum: 1, Lazy: false } repetition ||
            UnwrapTransparentGroups(repetition.Child) is not RegexSequenceNode { Nodes.Count: 2 } sequence)
        {
            return false;
        }

        return IsLiteral(sequence.Nodes[0], "?"u8) &&
            IsRepeatedClass(sequence.Nodes[1], @"^\s#"u8, minimum: 0, maximum: null);
    }

    private static bool IsOptionalFragment(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode { Minimum: 0, Maximum: 1, Lazy: false } repetition ||
            UnwrapTransparentGroups(repetition.Child) is not RegexSequenceNode { Nodes.Count: 2 } sequence)
        {
            return false;
        }

        return IsLiteral(sequence.Nodes[0], "#"u8) &&
            IsRepeatedClass(sequence.Nodes[1], @"^\s"u8, minimum: 0, maximum: null);
    }

    private static bool IsRepeatedClass(RegexSyntaxNode node, ReadOnlySpan<byte> expression, int minimum, int? maximum)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode repetition &&
            repetition.Minimum == minimum &&
            repetition.Maximum == maximum &&
            !repetition.Lazy &&
            IsClass(repetition.Child, expression);
    }

    private static bool IsLiteral(RegexSyntaxNode node, ReadOnlySpan<byte> literal)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Span.SequenceEqual(literal);
    }

    private static bool IsClass(RegexSyntaxNode node, ReadOnlySpan<byte> expression)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            atom.Value.Span.SequenceEqual(expression);
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
