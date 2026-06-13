namespace Scout;

internal sealed class RegexWordWhitespaceLiteralEngine
{
    private readonly byte[] literal;
    private readonly bool hasTrailingWord;
    private readonly bool hasLeadingBoundary;
    private readonly bool hasTrailingBoundary;
    private readonly RegexCompileOptions options;

    private RegexWordWhitespaceLiteralEngine(
        byte[] literal,
        bool hasTrailingWord,
        bool hasLeadingBoundary,
        bool hasTrailingBoundary,
        RegexCompileOptions options)
    {
        this.literal = literal;
        this.hasTrailingWord = hasTrailingWord;
        this.hasLeadingBoundary = hasLeadingBoundary;
        this.hasTrailingBoundary = hasTrailingBoundary;
        this.options = options;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexWordWhitespaceLiteralEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses)
        {
            return false;
        }

        if (!TryUnwrapWithOptions(root, options, out root, out RegexCompileOptions rootOptions) ||
            root is not RegexSequenceNode sequence ||
            !TryCollectSequenceItems(sequence, rootOptions, out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items))
        {
            return false;
        }

        int index = 0;
        bool hasLeadingBoundary = TryConsumeWordBoundary(items, ref index, out _);
        if (!TryConsumeRepeatedAtom(items, ref index, RegexSyntaxKind.WordClass, minimum: 1) ||
            !TryConsumeRepeatedAtom(items, ref index, RegexSyntaxKind.WhitespaceClass, minimum: 1) ||
            !TryConsumeLiteralRun(items, ref index, out byte[] literal))
        {
            return false;
        }

        bool hasTrailingWord = false;
        if (TryConsumeRepeatedAtom(items, ref index, RegexSyntaxKind.WhitespaceClass, minimum: 1))
        {
            if (!TryConsumeRepeatedAtom(items, ref index, RegexSyntaxKind.WordClass, minimum: 1))
            {
                return false;
            }

            hasTrailingWord = true;
        }

        bool hasTrailingBoundary = TryConsumeWordBoundary(items, ref index, out _);
        if (index != items.Count ||
            literal.Length == 0 ||
            hasLeadingBoundary != hasTrailingBoundary && !hasTrailingWord)
        {
            return false;
        }

        engine = new RegexWordWhitespaceLiteralEngine(
            literal,
            hasTrailingWord,
            hasLeadingBoundary,
            hasTrailingBoundary,
            rootOptions);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int search = lowerBound;
        while (search < haystack.Length)
        {
            int relative = haystack[search..].IndexOf(literal);
            if (relative < 0)
            {
                return null;
            }

            int literalStart = search + relative;
            if (TryMatchAroundLiteral(haystack, lowerBound, literalStart, out RegexMatch match))
            {
                return match;
            }

            search = literalStart + 1;
        }

        return null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            hasLeadingBoundary && !BoundaryMatches(haystack, start) ||
            !IsAsciiWord(haystack[start]))
        {
            return false;
        }

        int position = start + 1;
        while (position < haystack.Length && IsAsciiWord(haystack[position]))
        {
            position++;
        }

        int spaceStart = position;
        while (position < haystack.Length && IsRegexWhitespace(haystack[position]))
        {
            position++;
        }

        if (position == spaceStart ||
            literal.Length > haystack.Length - position ||
            !haystack.Slice(position, literal.Length).SequenceEqual(literal))
        {
            return false;
        }

        position += literal.Length;
        if (hasTrailingWord)
        {
            int trailingSpaceStart = position;
            while (position < haystack.Length && IsRegexWhitespace(haystack[position]))
            {
                position++;
            }

            if (position == trailingSpaceStart ||
                position >= haystack.Length ||
                !IsAsciiWord(haystack[position]))
            {
                return false;
            }

            do
            {
                position++;
            }
            while (position < haystack.Length && IsAsciiWord(haystack[position]));
        }

        if (hasTrailingBoundary && !BoundaryMatches(haystack, position))
        {
            return false;
        }

        length = position - start;
        return true;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (Find(haystack, offset) is RegexMatch match)
        {
            total += sumSpans ? match.Length : 1;
            offset = match.Length == 0
                ? Math.Min(match.End + 1, haystack.Length + 1)
                : match.End;
        }

        return total;
    }

    private bool TryMatchAroundLiteral(
        ReadOnlySpan<byte> haystack,
        int lowerBound,
        int literalStart,
        out RegexMatch match)
    {
        match = default;
        int literalEnd = literalStart + literal.Length;
        if (literalEnd > haystack.Length ||
            !TryWhitespaceEndingAt(haystack, lowerBound, literalStart, out int wordEnd) ||
            !TryWordEndingAt(haystack, lowerBound, wordEnd, out int wordStart))
        {
            return false;
        }

        int matchStart = wordStart;
        if (matchStart < lowerBound)
        {
            if (hasLeadingBoundary || lowerBound >= wordEnd || !IsAsciiWord(haystack[lowerBound]))
            {
                return false;
            }

            matchStart = lowerBound;
        }

        if (hasLeadingBoundary && !BoundaryMatches(haystack, matchStart))
        {
            return false;
        }

        int matchEnd = literalEnd;
        if (hasTrailingWord)
        {
            int trailingWordStart = ConsumeWhitespace(haystack, literalEnd);
            if (trailingWordStart == literalEnd ||
                trailingWordStart >= haystack.Length ||
                !IsAsciiWord(haystack[trailingWordStart]))
            {
                return false;
            }

            matchEnd = ConsumeWord(haystack, trailingWordStart + 1);
        }

        if (hasTrailingBoundary && !BoundaryMatches(haystack, matchEnd))
        {
            return false;
        }

        match = new RegexMatch(matchStart, matchEnd - matchStart);
        return true;
    }

    private bool BoundaryMatches(ReadOnlySpan<byte> haystack, int position)
    {
        return RegexByteClass.PredicateMatches(
            haystack,
            position,
            RegexSyntaxKind.WordBoundary,
            options.MultiLine,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses);
    }

    private static bool TryWhitespaceEndingAt(ReadOnlySpan<byte> haystack, int lowerBound, int end, out int start)
    {
        start = end;
        while (start > lowerBound && IsRegexWhitespace(haystack[start - 1]))
        {
            start--;
        }

        return start < end;
    }

    private static bool TryWordEndingAt(ReadOnlySpan<byte> haystack, int lowerBound, int end, out int start)
    {
        start = end;
        while (start > lowerBound && IsAsciiWord(haystack[start - 1]))
        {
            start--;
        }

        return start < end;
    }

    private static int ConsumeWhitespace(ReadOnlySpan<byte> haystack, int position)
    {
        while (position < haystack.Length && IsRegexWhitespace(haystack[position]))
        {
            position++;
        }

        return position;
    }

    private static int ConsumeWord(ReadOnlySpan<byte> haystack, int position)
    {
        while (position < haystack.Length && IsAsciiWord(haystack[position]))
        {
            position++;
        }

        return position;
    }

    private static bool TryConsumeRepeatedAtom(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index,
        RegexSyntaxKind atomKind,
        int minimum)
    {
        if (index >= items.Count ||
            !TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            unwrapped is not RegexRepetitionNode
            {
                Minimum: var actualMinimum,
                Maximum: null,
                Lazy: false,
            } repetition ||
            actualMinimum != minimum ||
            !TryUnwrapWithOptions(repetition.Child, effectiveOptions, out RegexSyntaxNode child, out RegexCompileOptions childOptions) ||
            childOptions.CaseInsensitive ||
            childOptions.Utf8 ||
            childOptions.UnicodeClasses ||
            child is not RegexAtomNode { Kind: var actualKind } ||
            actualKind != atomKind)
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeLiteralRun(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index,
        out byte[] literal)
    {
        literal = [];
        var bytes = new List<byte>();
        while (index < items.Count &&
            TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) &&
            !effectiveOptions.CaseInsensitive &&
            !effectiveOptions.Utf8 &&
            !effectiveOptions.UnicodeClasses &&
            unwrapped is RegexAtomNode
            {
                Kind: RegexSyntaxKind.Literal,
                Value.Length: 1,
            } atom)
        {
            bytes.Add(atom.Value.Span[0]);
            index++;
        }

        literal = bytes.ToArray();
        return literal.Length > 0;
    }

    private static bool TryConsumeWordBoundary(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index,
        out RegexCompileOptions boundaryOptions)
    {
        boundaryOptions = default;
        if (index >= items.Count ||
            !TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            unwrapped is not RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary })
        {
            return false;
        }

        boundaryOptions = effectiveOptions;
        index++;
        return true;
    }

    private static bool TryCollectSequenceItems(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items)
    {
        items = [];
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            items.Add((child, currentOptions));
        }

        return true;
    }

    private static bool TryUnwrapWithOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSyntaxNode unwrapped,
        out RegexCompileOptions effectiveOptions)
    {
        while (node is RegexGroupNode group)
        {
            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            node = group.Child;
        }

        unwrapped = node;
        effectiveOptions = options;
        return true;
    }

    private static bool IsAsciiWord(byte value)
    {
        return RegexSimpleSequenceSegment.IsAsciiWord(value);
    }

    private static bool IsRegexWhitespace(byte value)
    {
        return RegexSimpleSequenceSegment.IsRegexWhitespace(value);
    }
}
