namespace Scout;

internal sealed class RegexWholeLineEngine
{
    private readonly byte lineTerminator;

    private RegexWholeLineEngine(byte lineTerminator)
    {
        this.lineTerminator = lineTerminator;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexWholeLineEngine? engine)
    {
        engine = null;
        if (!TryCollectItems(root, options, out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items) ||
            items.Count != 3 ||
            !TryGetAnchor(items[0], RegexSyntaxKind.StartAnchor, out RegexCompileOptions startOptions) ||
            !TryGetGreedyDotStar(items[1], out RegexCompileOptions dotOptions) ||
            !TryGetAnchor(items[2], RegexSyntaxKind.EndAnchor, out RegexCompileOptions endOptions) ||
            !CanUseByteLineMode(startOptions) ||
            !CanUseByteLineMode(endOptions) ||
            !CanUseByteLineMode(dotOptions) ||
            !startOptions.MultiLine ||
            !endOptions.MultiLine ||
            dotOptions.DotMatchesNewline ||
            startOptions.LineTerminator != endOptions.LineTerminator ||
            startOptions.LineTerminator != dotOptions.LineTerminator)
        {
            return false;
        }

        engine = new RegexWholeLineEngine(startOptions.LineTerminator);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = FindLineStartAtOrAfter(haystack, Math.Clamp(startAt, 0, haystack.Length));
        if (start < 0)
        {
            return null;
        }

        int end = FindLineEnd(haystack, start);
        return new RegexMatch(start, end - start);
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (!IsLineStart(haystack, start))
        {
            return null;
        }

        int end = FindLineEnd(haystack, start);
        return new RegexMatch(start, end - start);
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int start = FindLineStartAtOrAfter(haystack, Math.Clamp(startAt, 0, haystack.Length));
        while (start >= 0)
        {
            int end = FindLineEnd(haystack, start);
            total += sumSpans ? end - start : 1;
            if (end >= haystack.Length)
            {
                return total;
            }

            start = end + 1;
        }

        return total;
    }

    private int FindLineStartAtOrAfter(ReadOnlySpan<byte> haystack, int start)
    {
        if (IsLineStart(haystack, start))
        {
            return start;
        }

        if (start >= haystack.Length)
        {
            return -1;
        }

        int offset = haystack[start..].IndexOf(lineTerminator);
        if (offset < 0)
        {
            return -1;
        }

        return start + offset + 1;
    }

    private bool IsLineStart(ReadOnlySpan<byte> haystack, int position)
    {
        return position == 0 ||
            position <= haystack.Length && haystack[position - 1] == lineTerminator;
    }

    private int FindLineEnd(ReadOnlySpan<byte> haystack, int start)
    {
        int offset = haystack[start..].IndexOf(lineTerminator);
        return offset < 0 ? haystack.Length : start + offset;
    }

    private static bool CanUseByteLineMode(RegexCompileOptions options)
    {
        return !options.CaseInsensitive &&
            !options.Crlf &&
            !options.Utf8 &&
            !options.UnicodeClasses;
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

    private static bool TryGetGreedyDotStar(
        (RegexSyntaxNode Node, RegexCompileOptions Options) item,
        out RegexCompileOptions options)
    {
        options = default;
        if (!TryUnwrapWithOptions(item.Node, item.Options, out RegexSyntaxNode node, out RegexCompileOptions repetitionOptions) ||
            node is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: null,
                Lazy: false,
            } repetition ||
            !TryUnwrapWithOptions(repetition.Child, repetitionOptions, out RegexSyntaxNode child, out options) ||
            child is not RegexAtomNode { Kind: RegexSyntaxKind.Dot })
        {
            return false;
        }

        return true;
    }

    private static bool TryCollectItems(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items)
    {
        items = [];
        if (!TryUnwrapWithOptions(root, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions))
        {
            return false;
        }

        if (unwrapped is RegexSequenceNode sequence)
        {
            return TryCollectSequenceItems(sequence, effectiveOptions, out items);
        }

        items.Add((unwrapped, effectiveOptions));
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
