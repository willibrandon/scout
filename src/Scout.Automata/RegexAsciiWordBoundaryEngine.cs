namespace Scout;

internal sealed class RegexAsciiWordBoundaryEngine
{
    private readonly int minimum;

    private RegexAsciiWordBoundaryEngine(int minimum)
    {
        this.minimum = minimum;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexAsciiWordBoundaryEngine? engine)
    {
        engine = null;
        if (options.Utf8 ||
            options.UnicodeClasses ||
            options.CaseInsensitive)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 3 } sequence ||
            !IsWordBoundary(sequence.Nodes[0]) ||
            !IsWordBoundary(sequence.Nodes[2]) ||
            UnwrapTransparentGroups(sequence.Nodes[1]) is not RegexRepetitionNode { Minimum: > 0, Maximum: null } repetition ||
            !IsAsciiWordAtom(repetition.Child, options))
        {
            return false;
        }

        engine = new RegexAsciiWordBoundaryEngine(repetition.Minimum);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryFind(haystack, Math.Clamp(startAt, 0, haystack.Length), out RegexMatch match)
            ? match
            : null;
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
        CountOrSum(haystack, startAt, sumSpans: false, out long total);
        return total;
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        CountOrSum(haystack, startAt, sumSpans: true, out long total);
        return total;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            start > 0 && IsAsciiWord(haystack[start - 1]) ||
            !IsAsciiWord(haystack[start]))
        {
            return false;
        }

        int end = start + 1;
        while (end < haystack.Length && IsAsciiWord(haystack[end]))
        {
            end++;
        }

        length = end - start;
        return length >= minimum;
    }

    private bool TryFind(ReadOnlySpan<byte> haystack, int startAt, out RegexMatch match)
    {
        int position = SkipPartialWord(haystack, startAt);
        while (position < haystack.Length)
        {
            while (position < haystack.Length && !IsAsciiWord(haystack[position]))
            {
                position++;
            }

            int start = position;
            while (position < haystack.Length && IsAsciiWord(haystack[position]))
            {
                position++;
            }

            int length = position - start;
            if (length >= minimum)
            {
                match = new RegexMatch(start, length);
                return true;
            }
        }

        match = default;
        return false;
    }

    private void CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, out long total)
    {
        total = 0;
        int position = SkipPartialWord(haystack, Math.Clamp(startAt, 0, haystack.Length));
        while (position < haystack.Length)
        {
            while (position < haystack.Length && !IsAsciiWord(haystack[position]))
            {
                position++;
            }

            int start = position;
            while (position < haystack.Length && IsAsciiWord(haystack[position]))
            {
                position++;
            }

            int length = position - start;
            if (length >= minimum)
            {
                total += sumSpans ? length : 1;
            }
        }
    }

    private static int SkipPartialWord(ReadOnlySpan<byte> haystack, int position)
    {
        if (position > 0 &&
            position < haystack.Length &&
            IsAsciiWord(haystack[position - 1]) &&
            IsAsciiWord(haystack[position]))
        {
            do
            {
                position++;
            }
            while (position < haystack.Length && IsAsciiWord(haystack[position]));
        }

        return position;
    }

    private static bool IsWordBoundary(RegexSyntaxNode node)
    {
        return UnwrapTransparentGroups(node) is RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary };
    }

    private static bool IsAsciiWordAtom(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode atom)
        {
            return false;
        }

        if (atom.Kind == RegexSyntaxKind.WordClass)
        {
            return true;
        }

        if (atom.Kind != RegexSyntaxKind.CharacterClass)
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
            if (matches != IsAsciiWord((byte)value))
            {
                return false;
            }
        }

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

    private static bool IsAsciiWord(byte value)
    {
        return RegexSimpleSequenceSegment.IsAsciiWord(value);
    }
}
