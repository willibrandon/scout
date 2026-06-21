namespace Scout;

internal sealed class RegexLh3EmailEngine
{
    private RegexLh3EmailEngine()
    {
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexLh3EmailEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            !IsEmailPattern(root, options))
        {
            return false;
        }

        engine = new RegexLh3EmailEngine();
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
        int at = start;
        while (at < haystack.Length && IsEmailByte(haystack[at]))
        {
            at++;
        }

        return at < haystack.Length &&
            haystack[at] == (byte)'@' &&
            TryMatchAroundAt(haystack, start, at, out int matchStart, out int length) &&
            matchStart == start
                ? new RegexMatch(start, length)
                : null;
    }

    public static bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        int searchAt = 0;
        while (searchAt < haystack.Length)
        {
            int relative = haystack[searchAt..].IndexOf((byte)'@');
            if (relative < 0)
            {
                return false;
            }

            int at = searchAt + relative;
            if (at > 0 &&
                at + 1 < haystack.Length &&
                IsEmailByte(haystack[at - 1]) &&
                IsEmailByte(haystack[at + 1]))
            {
                return true;
            }

            searchAt = at + 1;
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
            int relative = haystack[searchAt..].IndexOf((byte)'@');
            if (relative < 0)
            {
                match = default;
                return false;
            }

            int at = searchAt + relative;
            if (TryMatchAroundAt(haystack, minimumStart, at, out int start, out int length))
            {
                match = new RegexMatch(start, length);
                return true;
            }

            searchAt = at + 1;
        }

        match = default;
        return false;
    }

    private static bool TryMatchAroundAt(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        int at,
        out int start,
        out int length)
    {
        start = at - 1;
        while (start >= minimumStart && IsEmailByte(haystack[start]))
        {
            start--;
        }

        start++;
        if (at - start < 1)
        {
            length = 0;
            return false;
        }

        int domainStart = at + 1;
        if (domainStart >= haystack.Length || !IsEmailByte(haystack[domainStart]))
        {
            length = 0;
            return false;
        }

        int end = domainStart + 1;
        while (end < haystack.Length && IsEmailByte(haystack[end]))
        {
            end++;
        }

        length = end - start;
        return true;
    }

    private static bool IsEmailPattern(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsEmailCapture(sequence.Nodes[0], options) &&
            IsLiteral(sequence.Nodes[1], "@"u8) &&
            IsEmailCapture(sequence.Nodes[2], options);
    }

    private static bool IsEmailCapture(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group &&
            IsRepeatedClass(group.Child, options, IsEmailByte, minimum: 1, maximum: null);
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

    private static bool IsEmailByte(byte value)
    {
        return value is not (byte)' ' and not (byte)'@';
    }
}
