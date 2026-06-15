namespace Scout;

internal sealed class RegexLh3DateEngine
{
    private RegexLh3DateEngine()
    {
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexLh3DateEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            UnwrapTransparentGroups(root) is not RegexSequenceNode { Nodes.Count: 5 } sequence ||
            !IsOneOrTwoDigitCapture(sequence.Nodes[0], options) ||
            !IsLiteral(sequence.Nodes[1], "/"u8) ||
            !IsOneOrTwoDigitCapture(sequence.Nodes[2], options) ||
            !IsLiteral(sequence.Nodes[3], "/"u8) ||
            !IsTwoOrFourDigitYearCapture(sequence.Nodes[4], options))
        {
            return false;
        }

        engine = new RegexLh3DateEngine();
        return true;
    }

    public static RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int minimumStart = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = Math.Min(haystack.Length, minimumStart + 1);
        while (searchAt < haystack.Length)
        {
            int slashOffset = haystack[searchAt..].IndexOf((byte)'/');
            if (slashOffset < 0)
            {
                return null;
            }

            int slash = searchAt + slashOffset;
            if (TryMatchBeforeSlash(haystack, minimumStart, slash, out int start, out int length))
            {
                return new RegexMatch(start, length);
            }

            searchAt = slash + 1;
        }

        return null;
    }

    public static RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, start, out int length)
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
        while (Find(haystack, offset) is RegexMatch match)
        {
            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return total;
    }

    private static bool TryMatchBeforeSlash(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        int slash,
        out int start,
        out int length)
    {
        start = 0;
        length = 0;
        int twoDigitStart = slash - 2;
        if (twoDigitStart >= minimumStart &&
            TryMatchAt(haystack, twoDigitStart, out length))
        {
            start = twoDigitStart;
            return true;
        }

        int oneDigitStart = slash - 1;
        if (oneDigitStart >= minimumStart &&
            TryMatchAt(haystack, oneDigitStart, out length))
        {
            start = oneDigitStart;
            return true;
        }

        return false;
    }

    private static bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length || !IsAsciiDigit(haystack[start]))
        {
            return false;
        }

        int firstSlash = start + 1;
        if (firstSlash >= haystack.Length)
        {
            return false;
        }

        if (haystack[firstSlash] != (byte)'/')
        {
            if (firstSlash + 1 >= haystack.Length ||
                !IsAsciiDigit(haystack[firstSlash]) ||
                haystack[firstSlash + 1] != (byte)'/')
            {
                return false;
            }

            firstSlash++;
        }

        int dayStart = firstSlash + 1;
        if (dayStart >= haystack.Length || !IsAsciiDigit(haystack[dayStart]))
        {
            return false;
        }

        int secondSlash = dayStart + 1;
        if (secondSlash >= haystack.Length)
        {
            return false;
        }

        if (haystack[secondSlash] != (byte)'/')
        {
            if (secondSlash + 1 >= haystack.Length ||
                !IsAsciiDigit(haystack[secondSlash]) ||
                haystack[secondSlash + 1] != (byte)'/')
            {
                return false;
            }

            secondSlash++;
        }

        int yearStart = secondSlash + 1;
        if (yearStart + 1 >= haystack.Length ||
            !IsAsciiDigit(haystack[yearStart]) ||
            !IsAsciiDigit(haystack[yearStart + 1]))
        {
            return false;
        }

        int yearLength = yearStart + 3 < haystack.Length &&
            IsAsciiDigit(haystack[yearStart + 2]) &&
            IsAsciiDigit(haystack[yearStart + 3])
                ? 4
                : 2;
        length = yearStart + yearLength - start;
        return true;
    }

    private static bool IsOneOrTwoDigitCapture(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group &&
            UnwrapTransparentGroups(group.Child) is RegexSequenceNode { Nodes.Count: 2 } sequence &&
            IsDigitAtom(sequence.Nodes[0], options) &&
            IsOptionalDigit(sequence.Nodes[1], options);
    }

    private static bool IsTwoOrFourDigitYearCapture(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group ||
            UnwrapTransparentGroups(group.Child) is not RegexSequenceNode { Nodes.Count: 3 } sequence ||
            !IsDigitAtom(sequence.Nodes[0], options) ||
            !IsDigitAtom(sequence.Nodes[1], options) ||
            UnwrapTransparentGroups(sequence.Nodes[2]) is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: 1,
                Lazy: false,
            } optional ||
            UnwrapTransparentGroups(optional.Child) is not RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } inner ||
            UnwrapTransparentGroups(inner.Child) is not RegexSequenceNode { Nodes.Count: 2 } innerSequence)
        {
            return false;
        }

        return IsDigitAtom(innerSequence.Nodes[0], options) &&
            IsDigitAtom(innerSequence.Nodes[1], options);
    }

    private static bool IsOptionalDigit(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: 1,
                Lazy: false,
            } repetition &&
            IsDigitAtom(repetition.Child, options);
    }

    private static bool IsDigitAtom(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
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
            bool actual = RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                atom.Value.Span,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator);
            if (actual != IsAsciiDigit((byte)value))
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

    private static bool IsAsciiDigit(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }
}
