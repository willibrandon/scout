namespace Scout;

internal sealed class RegexDelimitedRunEngine
{
    private readonly RegexSimpleSequenceSegment left;
    private readonly RegexSimpleSequenceSegment right;
    private readonly byte delimiter;
    private readonly int leftMinimum;
    private readonly int rightMinimum;

    private RegexDelimitedRunEngine(
        RegexSimpleSequenceSegment left,
        RegexSimpleSequenceSegment right,
        byte delimiter,
        int leftMinimum,
        int rightMinimum)
    {
        this.left = left;
        this.right = right;
        this.delimiter = delimiter;
        this.leftMinimum = leftMinimum;
        this.rightMinimum = rightMinimum;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexDelimitedRunEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive || options.Utf8 || options.UnicodeClasses)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 3 } sequence ||
            !TryGetRepeatedByteAtom(sequence.Nodes[0], options, out RegexSimpleSequenceSegment left, out int leftMinimum) ||
            !TryGetDelimiter(sequence.Nodes[1], out byte delimiter) ||
            !TryGetRepeatedByteAtom(sequence.Nodes[2], options, out RegexSimpleSequenceSegment right, out int rightMinimum) ||
            left.AtomMatches(delimiter) ||
            right.AtomMatches(delimiter))
        {
            return false;
        }

        engine = new RegexDelimitedRunEngine(left, right, delimiter, leftMinimum, rightMinimum);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int search = Math.Min(haystack.Length, offset + leftMinimum);
        while (search < haystack.Length)
        {
            int relative = haystack[search..].IndexOf(delimiter);
            if (relative < 0)
            {
                return null;
            }

            int delimiterAt = search + relative;
            int leftStart = delimiterAt;
            int leftLength = 0;
            while (leftStart > offset && left.AtomMatches(haystack[leftStart - 1]))
            {
                leftStart--;
                leftLength++;
            }

            if (leftLength < leftMinimum)
            {
                search = delimiterAt + 1;
                continue;
            }

            int rightEnd = delimiterAt + 1;
            int rightLength = 0;
            while (rightEnd < haystack.Length && right.AtomMatches(haystack[rightEnd]))
            {
                rightEnd++;
                rightLength++;
            }

            if (rightLength >= rightMinimum)
            {
                return new RegexMatch(leftStart, rightEnd - leftStart);
            }

            search = delimiterAt + 1;
        }

        return null;
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
        int position = start;
        int leftLength = 0;
        while (position < haystack.Length && left.AtomMatches(haystack[position]))
        {
            position++;
            leftLength++;
        }

        if (leftLength < leftMinimum ||
            position >= haystack.Length ||
            haystack[position] != delimiter)
        {
            return false;
        }

        position++;
        int rightLength = 0;
        while (position < haystack.Length && right.AtomMatches(haystack[position]))
        {
            position++;
            rightLength++;
        }

        if (rightLength < rightMinimum)
        {
            return false;
        }

        length = position - start;
        return true;
    }

    private void CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, out long total)
    {
        total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (offset <= haystack.Length)
        {
            RegexMatch? match = Find(haystack, offset);
            if (!match.HasValue)
            {
                return;
            }

            total += sumSpans ? match.Value.Length : 1;
            offset = match.Value.End;
        }
    }

    private static bool TryGetRepeatedByteAtom(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSimpleSequenceSegment segment,
        out int minimum)
    {
        segment = default;
        minimum = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: null,
                Lazy: false,
            } repetition ||
            UnwrapTransparentGroups(repetition.Child) is not RegexAtomNode atom ||
            !IsByteAtom(atom) ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        segment = new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            minimum: 1,
            maximum: 1,
            lazy: false);
        minimum = repetition.Minimum;
        return true;
    }

    private static bool TryGetDelimiter(RegexSyntaxNode node, out byte delimiter)
    {
        delimiter = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode
            {
                Kind: RegexSyntaxKind.Literal,
                Value.Length: 1,
            } atom)
        {
            return false;
        }

        delimiter = atom.Value.Span[0];
        return true;
    }

    private static bool IsByteAtom(RegexAtomNode atom)
    {
        if (atom.Kind == RegexSyntaxKind.Literal)
        {
            return atom.Value.Length == 1;
        }

        return atom.Kind is RegexSyntaxKind.Dot
            or RegexSyntaxKind.AnyClass
            or RegexSyntaxKind.CharacterClass
            or RegexSyntaxKind.DigitClass
            or RegexSyntaxKind.NotDigitClass
            or RegexSyntaxKind.WordClass
            or RegexSyntaxKind.NotWordClass
            or RegexSyntaxKind.WhitespaceClass
            or RegexSyntaxKind.NotWhitespaceClass;
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
