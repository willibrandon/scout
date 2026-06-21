namespace Scout;

internal sealed class RegexUnicodeWordWhitespaceLiteralEngine
{
    private readonly byte[] literal;
    private readonly bool hasTrailingWord;
    private readonly bool hasLeadingBoundary;
    private readonly bool hasTrailingBoundary;
    private readonly RegexCompileOptions options;
    private readonly MemmemFinder literalFinder;

    private RegexUnicodeWordWhitespaceLiteralEngine(
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
        literalFinder = new MemmemFinder(literal);
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexUnicodeWordWhitespaceLiteralEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            !options.UnicodeClasses)
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
        bool hasLeadingBoundary = TryConsumeWordBoundary(items, ref index);
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

        bool hasTrailingBoundary = TryConsumeWordBoundary(items, ref index);
        if (index != items.Count ||
            literal.Length == 0 ||
            hasLeadingBoundary != hasTrailingBoundary && !hasTrailingWord)
        {
            return false;
        }

        engine = new RegexUnicodeWordWhitespaceLiteralEngine(
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
        int searchAt = lowerBound;
        while (searchAt < haystack.Length)
        {
            int relative = literalFinder.Find(haystack[searchAt..]);
            if (relative < 0)
            {
                return null;
            }

            int literalStart = searchAt + relative;
            if (TryMatchAroundLiteral(haystack, lowerBound, literalStart, out RegexMatch match))
            {
                return match;
            }

            searchAt = literalStart + 1;
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
            !TryWordMatchLength(haystack, start, out int scalarLength))
        {
            return false;
        }

        int position = start + scalarLength;
        position = ConsumeAtom(haystack, position, RegexSyntaxKind.WordClass);
        int spaceStart = position;
        position = ConsumeAtom(haystack, position, RegexSyntaxKind.WhitespaceClass);
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
            position = ConsumeAtom(haystack, position, RegexSyntaxKind.WhitespaceClass);
            if (position == trailingSpaceStart ||
                !TryWordMatchLength(haystack, position, out scalarLength))
            {
                return false;
            }

            position = ConsumeAtom(haystack, position + scalarLength, RegexSyntaxKind.WordClass);
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
            offset = match.End;
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
            !TryAtomRunEndingAt(haystack, lowerBound, literalStart, RegexSyntaxKind.WhitespaceClass, out int wordEnd) ||
            !TryAtomRunEndingAt(haystack, lowerBound, wordEnd, RegexSyntaxKind.WordClass, out int wordStart))
        {
            return false;
        }

        int matchStart = wordStart;
        if (matchStart < lowerBound)
        {
            if (hasLeadingBoundary ||
                lowerBound >= wordEnd ||
                !TryWordMatchLength(haystack, lowerBound, out _))
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
            int trailingWordStart = ConsumeAtom(haystack, literalEnd, RegexSyntaxKind.WhitespaceClass);
            if (trailingWordStart == literalEnd ||
                !TryWordMatchLength(haystack, trailingWordStart, out int scalarLength))
            {
                return false;
            }

            matchEnd = ConsumeAtom(haystack, trailingWordStart + scalarLength, RegexSyntaxKind.WordClass);
        }

        if (hasTrailingBoundary && !BoundaryMatches(haystack, matchEnd))
        {
            return false;
        }

        match = new RegexMatch(matchStart, matchEnd - matchStart);
        return true;
    }

    private bool TryAtomRunEndingAt(
        ReadOnlySpan<byte> haystack,
        int lowerBound,
        int end,
        RegexSyntaxKind atomKind,
        out int start)
    {
        start = end;
        while (start > lowerBound && TryPreviousAtomMatchStart(haystack, start, atomKind, out int previousStart))
        {
            start = previousStart;
        }

        return start < end;
    }

    private int ConsumeAtom(ReadOnlySpan<byte> haystack, int position, RegexSyntaxKind atomKind)
    {
        while (TryAtomMatchLength(haystack, position, atomKind, out int length))
        {
            position += length;
        }

        return position;
    }

    private bool TryPreviousAtomMatchStart(
        ReadOnlySpan<byte> haystack,
        int end,
        RegexSyntaxKind atomKind,
        out int start)
    {
        start = 0;
        if (end <= 0)
        {
            return false;
        }

        int firstCandidate = Math.Max(0, end - 4);
        for (int candidate = end - 1; candidate >= firstCandidate; candidate--)
        {
            if (TryAtomMatchLength(haystack, candidate, atomKind, out int length) &&
                candidate + length == end)
            {
                start = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryWordMatchLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        return TryAtomMatchLength(haystack, position, RegexSyntaxKind.WordClass, out length);
    }

    private bool TryAtomMatchLength(ReadOnlySpan<byte> haystack, int position, RegexSyntaxKind atomKind, out int length)
    {
        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            atomKind,
            ReadOnlySpan<byte>.Empty,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out length);
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
            !childOptions.UnicodeClasses ||
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
            effectiveOptions.UnicodeClasses &&
            unwrapped is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
        {
            bytes.AddRange(atom.Value.Span.ToArray());
            index++;
        }

        literal = bytes.ToArray();
        return literal.Length > 0;
    }

    private static bool TryConsumeWordBoundary(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index)
    {
        if (index >= items.Count ||
            !TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            effectiveOptions.CaseInsensitive ||
            effectiveOptions.Utf8 ||
            !effectiveOptions.UnicodeClasses ||
            unwrapped is not RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary })
        {
            return false;
        }

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
}
