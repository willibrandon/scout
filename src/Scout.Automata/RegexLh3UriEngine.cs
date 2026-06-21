namespace Scout;

internal sealed class RegexLh3UriEngine
{
    private RegexLh3UriEngine()
    {
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexLh3UriEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            UnwrapTransparentGroups(root) is not RegexSequenceNode { Nodes.Count: 6 } sequence ||
            !IsSchemeCapture(sequence.Nodes[0], options) ||
            !IsLiteral(sequence.Nodes[1], ":"u8) ||
            !IsLiteral(sequence.Nodes[2], "/"u8) ||
            !IsLiteral(sequence.Nodes[3], "/"u8) ||
            !IsAuthorityCapture(sequence.Nodes[4], options) ||
            !IsOptionalPathCapture(sequence.Nodes[5], options))
        {
            return false;
        }

        engine = new RegexLh3UriEngine();
        return true;
    }

    public static RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryFind(haystack, Math.Clamp(startAt, 0, haystack.Length), out RegexMatch match)
            ? match
            : null;
    }

    public static RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int colon = start + 1;
        while (colon < haystack.Length && IsSchemeRest(haystack[colon]))
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

    public static bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        int searchAt = 0;
        while (searchAt < haystack.Length)
        {
            int colon = FindSchemeDelimiter(haystack, searchAt);
            if (colon < 0)
            {
                return false;
            }

            if (CanMatchAroundDelimiter(haystack, colon))
            {
                return true;
            }

            searchAt = colon + 1;
        }

        return false;
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
        while (TryFind(haystack, offset, out RegexMatch match))
        {
            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return total;
    }

    private static bool TryFind(ReadOnlySpan<byte> haystack, int minimumStart, out RegexMatch match)
    {
        int searchAt = minimumStart;
        while (searchAt < haystack.Length)
        {
            int colon = FindSchemeDelimiter(haystack, searchAt);
            if (colon < 0)
            {
                match = default;
                return false;
            }

            if (TryMatchAroundDelimiter(haystack, minimumStart, colon, out int start, out int length))
            {
                match = new RegexMatch(start, length);
                return true;
            }

            searchAt = colon + 1;
        }

        match = default;
        return false;
    }

    private static int FindSchemeDelimiter(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt <= haystack.Length - 3)
        {
            int relative = haystack[searchAt..].IndexOf((byte)':');
            if (relative < 0)
            {
                return -1;
            }

            int colon = searchAt + relative;
            if (colon + 2 < haystack.Length &&
                haystack[colon + 1] == (byte)'/' &&
                haystack[colon + 2] == (byte)'/')
            {
                return colon;
            }

            searchAt = colon + 1;
        }

        return -1;
    }

    private static bool TryMatchAroundDelimiter(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        int colon,
        out int start,
        out int length)
    {
        start = colon - 1;
        while (start >= minimumStart && IsSchemeRest(haystack[start]))
        {
            start--;
        }

        start++;
        while (start < colon && !IsAsciiLetter(haystack[start]))
        {
            start++;
        }

        if (start >= colon)
        {
            length = 0;
            return false;
        }

        int authorityStart = colon + 3;
        if (authorityStart >= haystack.Length || !IsAuthorityByte(haystack[authorityStart]))
        {
            length = 0;
            return false;
        }

        int end = authorityStart + 1;
        while (end < haystack.Length && IsAuthorityByte(haystack[end]))
        {
            end++;
        }

        if (end < haystack.Length && haystack[end] == (byte)'/')
        {
            end++;
            while (end < haystack.Length && haystack[end] != (byte)' ')
            {
                end++;
            }
        }

        length = end - start;
        return true;
    }

    private static bool CanMatchAroundDelimiter(ReadOnlySpan<byte> haystack, int colon)
    {
        int start = colon - 1;
        while (start >= 0 && IsSchemeRest(haystack[start]))
        {
            start--;
        }

        start++;
        while (start < colon && !IsAsciiLetter(haystack[start]))
        {
            start++;
        }

        int authorityStart = colon + 3;
        return start < colon &&
            authorityStart < haystack.Length &&
            IsAuthorityByte(haystack[authorityStart]);
    }

    private static bool IsSchemeCapture(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group ||
            UnwrapTransparentGroups(group.Child) is not RegexSequenceNode { Nodes.Count: 2 } sequence)
        {
            return false;
        }

        return IsClass(sequence.Nodes[0], options, IsAsciiLetter) &&
            IsRepeatedClass(sequence.Nodes[1], options, IsSchemeRest, minimum: 0, maximum: null);
    }

    private static bool IsAuthorityCapture(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group &&
            IsRepeatedClass(group.Child, options, IsAuthorityByte, minimum: 1, maximum: null);
    }

    private static bool IsOptionalPathCapture(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: 1,
                Lazy: false,
            } repetition ||
            UnwrapTransparentGroups(repetition.Child) is not RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group ||
            UnwrapTransparentGroups(group.Child) is not RegexSequenceNode { Nodes.Count: 2 } sequence)
        {
            return false;
        }

        return IsLiteral(sequence.Nodes[0], "/"u8) &&
            IsRepeatedClass(sequence.Nodes[1], options, IsPathByte, minimum: 0, maximum: null);
    }

    private static bool IsRepeatedClass(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        Func<byte, bool> predicate,
        int minimum,
        int? maximum)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode repetition &&
            repetition.Minimum == minimum &&
            repetition.Maximum == maximum &&
            !repetition.Lazy &&
            IsClass(repetition.Child, options, predicate);
    }

    private static bool IsClass(RegexSyntaxNode node, RegexCompileOptions options, Func<byte, bool> predicate)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom)
        {
            return false;
        }

        ReadOnlySpan<byte> expression = atom.Value.Span;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bool matches = RegexByteClass.AtomMatches(
                (byte)value,
                RegexSyntaxKind.CharacterClass,
                expression,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator);
            if (matches != predicate((byte)value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLiteral(RegexSyntaxNode node, ReadOnlySpan<byte> literal)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Span.SequenceEqual(literal);
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }

    private static bool IsSchemeRest(byte value)
    {
        return IsAsciiLetter(value) || value is >= (byte)'0' and <= (byte)'9';
    }

    private static bool IsAsciiLetter(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
    }

    private static bool IsAuthorityByte(byte value)
    {
        return value is not (byte)' ' and not (byte)'/';
    }

    private static bool IsPathByte(byte value)
    {
        return value != (byte)' ';
    }
}
