namespace Scout;

internal sealed class RegexWordSuffixLiteralEngine
{
    private readonly byte[] suffix;
    private readonly int prefixMinimum;
    private readonly int minimumWordLength;

    private RegexWordSuffixLiteralEngine(byte[] suffix, int prefixMinimum)
    {
        this.suffix = suffix;
        this.prefixMinimum = prefixMinimum;
        minimumWordLength = prefixMinimum + suffix.Length;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexWordSuffixLiteralEngine? engine)
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
        if (!TryConsumeWordBoundary(items, ref index) ||
            !TryConsumeRepeatedWord(items, ref index, out int prefixMinimum) ||
            !TryConsumeLiteralRun(items, ref index, out byte[] suffix) ||
            !TryConsumeWordBoundary(items, ref index) ||
            index != items.Count)
        {
            return false;
        }

        engine = new RegexWordSuffixLiteralEngine(suffix, prefixMinimum);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindNext(haystack, offset, out RegexMatch match))
        {
            return match;
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

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, start, out int length)
            ? new RegexMatch(start, length)
            : null;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            IsWordBefore(haystack, start) ||
            !IsAsciiWord(haystack[start]))
        {
            return false;
        }

        int end = ConsumeWord(haystack, start + 1);
        if (WordEndsWithSuffix(haystack, start, end))
        {
            length = end - start;
            return true;
        }

        return false;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindNext(haystack, offset, out RegexMatch match))
        {
            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return total;
    }

    private bool TryFindNext(ReadOnlySpan<byte> haystack, int offset, out RegexMatch match)
    {
        int search = offset;
        while (search <= haystack.Length - suffix.Length)
        {
            int relative = haystack[search..].IndexOf(suffix);
            if (relative < 0)
            {
                match = default;
                return false;
            }

            int suffixStart = search + relative;
            int end = suffixStart + suffix.Length;
            if (IsWordEnd(haystack, end) &&
                TryGetWordStartEndingAt(haystack, suffixStart, out int start) &&
                start >= offset &&
                suffixStart - start >= prefixMinimum)
            {
                match = new RegexMatch(start, end - start);
                return true;
            }

            search = end < haystack.Length && IsAsciiWord(haystack[end])
                ? suffixStart + 1
                : end + 1;
        }

        match = default;
        return false;
    }

    private bool WordEndsWithSuffix(ReadOnlySpan<byte> haystack, int start, int end)
    {
        int length = end - start;
        return length >= minimumWordLength &&
            haystack.Slice(end - suffix.Length, suffix.Length).SequenceEqual(suffix);
    }

    private static bool TryGetWordStartEndingAt(ReadOnlySpan<byte> haystack, int end, out int start)
    {
        start = end;
        while (start > 0 && IsAsciiWord(haystack[start - 1]))
        {
            start--;
        }

        return start < end;
    }

    private static bool IsWordEnd(ReadOnlySpan<byte> haystack, int position)
    {
        return position == haystack.Length || !IsAsciiWord(haystack[position]);
    }

    private static int ConsumeWord(ReadOnlySpan<byte> haystack, int position)
    {
        while (position < haystack.Length && IsAsciiWord(haystack[position]))
        {
            position++;
        }

        return position;
    }

    private static bool IsWordBefore(ReadOnlySpan<byte> haystack, int position)
    {
        return position > 0 && IsAsciiWord(haystack[position - 1]);
    }

    private static bool TryConsumeRepeatedWord(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index,
        out int minimum)
    {
        minimum = 0;
        if (index >= items.Count ||
            !TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            effectiveOptions.CaseInsensitive ||
            effectiveOptions.Utf8 ||
            effectiveOptions.UnicodeClasses ||
            unwrapped is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: null,
                Lazy: false,
            } repetition ||
            !TryUnwrapWithOptions(repetition.Child, effectiveOptions, out RegexSyntaxNode child, out RegexCompileOptions childOptions) ||
            childOptions.CaseInsensitive ||
            childOptions.Utf8 ||
            childOptions.UnicodeClasses ||
            child is not RegexAtomNode { Kind: RegexSyntaxKind.WordClass })
        {
            return false;
        }

        minimum = repetition.Minimum;
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
            unwrapped is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
        {
            ReadOnlySpan<byte> value = atom.Value.Span;
            for (int valueIndex = 0; valueIndex < value.Length; valueIndex++)
            {
                if (!IsAsciiWord(value[valueIndex]))
                {
                    literal = [];
                    return false;
                }

                bytes.Add(value[valueIndex]);
            }

            index++;
        }

        literal = bytes.ToArray();
        return literal.Length > 0;
    }

    private static bool TryConsumeWordBoundary(List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items, ref int index)
    {
        if (index >= items.Count ||
            !TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            effectiveOptions.CaseInsensitive ||
            effectiveOptions.Utf8 ||
            effectiveOptions.UnicodeClasses ||
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

    private static bool IsAsciiWord(byte value)
    {
        return RegexSimpleSequenceSegment.IsAsciiWord(value);
    }
}
