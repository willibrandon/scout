namespace Scout;

internal sealed class RegexLineBoundaryLiteralEngine
{
    private const int LineStartBranch = 1;
    private const int LineEndBranch = 2;

    private readonly byte[] literal;
    private readonly MemmemFinder finder;
    private readonly byte lineTerminator;

    private RegexLineBoundaryLiteralEngine(byte[] literal, byte lineTerminator)
    {
        this.literal = literal;
        finder = new MemmemFinder(literal);
        this.lineTerminator = lineTerminator;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexLineBoundaryLiteralEngine? engine)
    {
        engine = null;
        if (!CanUseByteLineMode(options) ||
            !TryUnwrapWithOptions(root, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            unwrapped is not RegexAlternationNode { Alternatives.Count: 2 } alternation)
        {
            return false;
        }

        RegexCompileOptions branchOptions = effectiveOptions;
        int firstSkipCount = 0;
        if (TryApplyLeadingInlineFlags(
            alternation.Alternatives[0],
            effectiveOptions,
            out RegexCompileOptions promotedOptions,
            out int promotedSkipCount))
        {
            branchOptions = promotedOptions;
            firstSkipCount = promotedSkipCount;
        }

        if (!TryGetBranch(
                alternation.Alternatives[0],
                branchOptions,
                firstSkipCount,
                out int firstKind,
                out byte[] firstLiteral,
                out byte firstLineTerminator) ||
            !TryGetBranch(
                alternation.Alternatives[1],
                branchOptions,
                skipLeadingInlineFlags: 0,
                out int secondKind,
                out byte[] secondLiteral,
                out byte secondLineTerminator) ||
            firstKind == secondKind ||
            firstLineTerminator != secondLineTerminator ||
            !firstLiteral.AsSpan().SequenceEqual(secondLiteral))
        {
            return false;
        }

        engine = new RegexLineBoundaryLiteralEngine(firstLiteral, firstLineTerminator);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindLiteral(haystack, searchAt, out int start))
        {
            if (IsBoundaryMatch(haystack, start))
            {
                return new RegexMatch(start, literal.Length);
            }

            searchAt = start + 1;
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return LiteralMatchesAt(haystack, start) && IsBoundaryMatch(haystack, start)
            ? new RegexMatch(start, literal.Length)
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
        if (LiteralMatchesAt(haystack, start) && IsBoundaryMatch(haystack, start))
        {
            length = literal.Length;
            return true;
        }

        length = 0;
        return false;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindLiteral(haystack, searchAt, out int start))
        {
            if (IsBoundaryMatch(haystack, start))
            {
                total += sumSpans ? literal.Length : 1;
                searchAt = start + literal.Length;
                continue;
            }

            searchAt = start + 1;
        }

        return total;
    }

    private bool TryFindLiteral(ReadOnlySpan<byte> haystack, int startAt, out int start)
    {
        int offset = finder.Find(haystack[startAt..]);
        if (offset < 0)
        {
            start = -1;
            return false;
        }

        start = startAt + offset;
        return true;
    }

    private bool IsBoundaryMatch(ReadOnlySpan<byte> haystack, int start)
    {
        return IsLineStart(haystack, start) || IsLineEnd(haystack, start + literal.Length);
    }

    private bool LiteralMatchesAt(ReadOnlySpan<byte> haystack, int start)
    {
        return (uint)start <= (uint)haystack.Length &&
            literal.Length <= haystack.Length - start &&
            haystack.Slice(start, literal.Length).SequenceEqual(literal);
    }

    private bool IsLineStart(ReadOnlySpan<byte> haystack, int position)
    {
        return position == 0 ||
            position <= haystack.Length && haystack[position - 1] == lineTerminator;
    }

    private bool IsLineEnd(ReadOnlySpan<byte> haystack, int position)
    {
        return position == haystack.Length ||
            position < haystack.Length && haystack[position] == lineTerminator;
    }

    private static bool TryGetBranch(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        int skipLeadingInlineFlags,
        out int kind,
        out byte[] literal,
        out byte lineTerminator)
    {
        kind = 0;
        literal = [];
        lineTerminator = 0;
        if (!TryCollectItems(node, options, skipLeadingInlineFlags, out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items) ||
            items.Count < 2)
        {
            return false;
        }

        if (TryGetAnchor(items[0], RegexSyntaxKind.StartAnchor, out RegexCompileOptions startOptions) &&
            TryCollectLiteral(items, 1, items.Count, out byte[] startLiteral) &&
            CanUseByteLineMode(startOptions) &&
            startOptions.MultiLine &&
            startLiteral.Length > 0)
        {
            kind = LineStartBranch;
            literal = startLiteral;
            lineTerminator = startOptions.LineTerminator;
            return true;
        }

        if (TryGetAnchor(items[^1], RegexSyntaxKind.EndAnchor, out RegexCompileOptions endOptions) &&
            TryCollectLiteral(items, 0, items.Count - 1, out byte[] endLiteral) &&
            CanUseByteLineMode(endOptions) &&
            endOptions.MultiLine &&
            endLiteral.Length > 0)
        {
            kind = LineEndBranch;
            literal = endLiteral;
            lineTerminator = endOptions.LineTerminator;
            return true;
        }

        return false;
    }

    private static bool TryCollectLiteral(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        int start,
        int end,
        out byte[] literal)
    {
        literal = [];
        var bytes = new List<byte>();
        for (int index = start; index < end; index++)
        {
            if (!TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode node, out RegexCompileOptions options) ||
                !CanUseByteLineMode(options) ||
                node is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
            {
                return false;
            }

            bytes.AddRange(atom.Value.ToArray());
        }

        literal = bytes.ToArray();
        return true;
    }

    private static bool TryGetAnchor(
        (RegexSyntaxNode Node, RegexCompileOptions Options) item,
        RegexSyntaxKind kind,
        out RegexCompileOptions options)
    {
        options = default;
        if (!TryUnwrapWithOptions(item.Node, item.Options, out RegexSyntaxNode node, out options) ||
            node is not RegexAtomNode { Kind: var actualKind } ||
            actualKind != kind)
        {
            return false;
        }

        return true;
    }

    private static bool TryCollectItems(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int skipLeadingInlineFlags,
        out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items)
    {
        items = [];
        if (!TryUnwrapWithOptions(root, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions))
        {
            return false;
        }

        if (unwrapped is RegexSequenceNode sequence)
        {
            RegexCompileOptions currentOptions = effectiveOptions;
            for (int index = 0; index < sequence.Nodes.Count; index++)
            {
                RegexSyntaxNode child = sequence.Nodes[index];
                if (index < skipLeadingInlineFlags &&
                    child is RegexInlineFlagsNode)
                {
                    continue;
                }

                if (child is RegexInlineFlagsNode flags)
                {
                    currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                    continue;
                }

                items.Add((child, currentOptions));
            }

            return true;
        }

        items.Add((unwrapped, effectiveOptions));
        return true;
    }

    private static bool TryApplyLeadingInlineFlags(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexCompileOptions promotedOptions,
        out int skipCount)
    {
        promotedOptions = options;
        skipCount = 0;
        if (!TryUnwrapWithOptions(node, options, out RegexSyntaxNode unwrapped, out promotedOptions) ||
            unwrapped is not RegexSequenceNode sequence)
        {
            return false;
        }

        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            if (sequence.Nodes[index] is not RegexInlineFlagsNode flags)
            {
                break;
            }

            promotedOptions = promotedOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
            skipCount++;
        }

        return skipCount > 0;
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

    private static bool CanUseByteLineMode(RegexCompileOptions options)
    {
        return !options.CaseInsensitive &&
            !options.Crlf &&
            !options.Utf8 &&
            !options.UnicodeClasses;
    }

}
