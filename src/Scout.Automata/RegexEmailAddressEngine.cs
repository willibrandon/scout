namespace Scout;

internal sealed class RegexEmailAddressEngine
{
    private RegexEmailAddressEngine()
    {
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexEmailAddressEngine? engine)
    {
        engine = null;
        if (options.Utf8 ||
            options.UnicodeClasses ||
            options.CaseInsensitive ||
            !IsEmailPattern(root))
        {
            return false;
        }

        engine = new RegexEmailAddressEngine();
        return true;
    }

    public static RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt < haystack.Length)
        {
            int relative = haystack[searchAt..].IndexOf((byte)'@');
            if (relative < 0)
            {
                return null;
            }

            int at = searchAt + relative;
            if (TryMatchAroundAt(haystack, Math.Clamp(startAt, 0, haystack.Length), at, out int start, out int length))
            {
                return new RegexMatch(start, length);
            }

            searchAt = at + 1;
        }

        return null;
    }

    public static RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int at = start;
        while (at < haystack.Length && IsLocalByte(haystack[at]))
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
            int relative = haystack[searchAt..].IndexOf((byte)'@');
            if (relative < 0)
            {
                return total;
            }

            int at = searchAt + relative;
            if (TryMatchAroundAt(haystack, offset, at, out int start, out int length))
            {
                total += sumSpans ? length : 1;
                offset = start + length;
                searchAt = offset;
                continue;
            }

            searchAt = at + 1;
        }

        return total;
    }

    private static bool TryMatchAroundAt(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        int at,
        out int start,
        out int length)
    {
        start = at - 1;
        while (start >= minimumStart && IsLocalByte(haystack[start]))
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
        if (domainStart >= haystack.Length || !IsDomainByte(haystack[domainStart]))
        {
            length = 0;
            return false;
        }

        int domainEnd = domainStart + 1;
        while (domainEnd < haystack.Length && IsDomainByte(haystack[domainEnd]))
        {
            domainEnd++;
        }

        int dot = haystack[domainStart..domainEnd].LastIndexOf((byte)'.');
        if (dot <= 0 || dot == domainEnd - domainStart - 1)
        {
            length = 0;
            return false;
        }

        length = domainEnd - start;
        return true;
    }

    private static bool IsLocalByte(byte value)
    {
        return IsAsciiWord(value) ||
            value is (byte)'.' or (byte)'+' or (byte)'-';
    }

    private static bool IsDomainByte(byte value)
    {
        return IsAsciiWord(value) ||
            value is (byte)'.' or (byte)'-';
    }

    private static bool IsAsciiWord(byte value)
    {
        return RegexSimpleSequenceSegment.IsAsciiLetter(value) ||
            RegexSimpleSequenceSegment.IsAsciiDigit(value) ||
            value == (byte)'_';
    }

    private static bool IsEmailPattern(RegexSyntaxNode root)
    {
        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 5 } sequence)
        {
            return false;
        }

        return IsRepeatedClass(sequence.Nodes[0], @"\w\.+-"u8) &&
            IsLiteral(sequence.Nodes[1], "@"u8) &&
            IsRepeatedClass(sequence.Nodes[2], @"\w\.-"u8) &&
            IsLiteral(sequence.Nodes[3], "."u8) &&
            IsRepeatedClass(sequence.Nodes[4], @"\w\.-"u8);
    }

    private static bool IsRepeatedClass(RegexSyntaxNode node, ReadOnlySpan<byte> expression)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode { Minimum: 1, Maximum: null, Lazy: false } repetition &&
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
