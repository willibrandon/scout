namespace Scout;

internal sealed class RegexLinePrefixCaptureEngine
{
    private readonly byte[] marker;
    private readonly int leadingCaptureIndex;
    private readonly int trailingCaptureIndex;
    private readonly int captureCount;
    private readonly RegexCompileOptions options;

    private RegexLinePrefixCaptureEngine(
        byte[] marker,
        int leadingCaptureIndex,
        int trailingCaptureIndex,
        int captureCount,
        RegexCompileOptions options)
    {
        this.marker = marker;
        this.leadingCaptureIndex = leadingCaptureIndex;
        this.trailingCaptureIndex = trailingCaptureIndex;
        this.captureCount = captureCount;
        this.options = options;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexLinePrefixCaptureEngine? engine)
    {
        engine = null;
        if (captureCount != 2 ||
            options.CaseInsensitive ||
            options.SwapGreed ||
            options.MultiLine ||
            options.DotMatchesNewline ||
            options.Crlf ||
            options.Utf8)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexSequenceNode sequence ||
            sequence.Nodes.Count < 4 ||
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[0]) is not RegexAtomNode { Kind: RegexSyntaxKind.StartAnchor } ||
            !TryGetRepeatedCapture(sequence.Nodes[1], RegexSyntaxKind.WhitespaceClass, out int leadingCaptureIndex) ||
            !TryCollectMarker(sequence.Nodes, start: 2, end: sequence.Nodes.Count - 1, out byte[] marker) ||
            !TryGetRepeatedCapture(sequence.Nodes[^1], RegexSyntaxKind.Dot, out int trailingCaptureIndex) ||
            marker.Length == 0)
        {
            return false;
        }

        engine = new RegexLinePrefixCaptureEngine(
            marker,
            leadingCaptureIndex,
            trailingCaptureIndex,
            captureCount,
            options);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!TryMatch(
            haystack,
            startAt,
            out RegexMatch match,
            out RegexMatch leading,
            out RegexMatch trailing))
        {
            return null;
        }

        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match;
        groups[leadingCaptureIndex] = leading;
        groups[trailingCaptureIndex] = trailing;
        return new RegexCaptures(match, groups);
    }

    public long CountCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatch(haystack, startAt, out _, out _, out _) ? 3 : 0;
    }

    private bool TryMatch(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out RegexMatch match,
        out RegexMatch leading,
        out RegexMatch trailing)
    {
        match = default;
        leading = default;
        trailing = default;
        if (Math.Clamp(startAt, 0, haystack.Length) != 0)
        {
            return false;
        }

        int position = ConsumeWhitespace(haystack, 0);
        if (marker.Length > haystack.Length - position ||
            !haystack.Slice(position, marker.Length).SequenceEqual(marker))
        {
            return false;
        }

        int trailingStart = position + marker.Length;
        int trailingEnd = FindDotStarEnd(haystack, trailingStart);
        match = new RegexMatch(0, trailingEnd);
        leading = new RegexMatch(0, position);
        trailing = new RegexMatch(trailingStart, trailingEnd - trailingStart);
        return true;
    }

    private int ConsumeWhitespace(ReadOnlySpan<byte> haystack, int position)
    {
        while (TryWhitespaceMatchLength(haystack, position, out int length))
        {
            position += length;
        }

        return position;
    }

    private bool TryWhitespaceMatchLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        if (position >= haystack.Length)
        {
            return false;
        }

        byte value = haystack[position];
        if (value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b)
        {
            length = 1;
            return true;
        }

        if (value <= 0x7F)
        {
            return false;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            RegexSyntaxKind.WhitespaceClass,
            default,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out length);
    }

    private int FindDotStarEnd(ReadOnlySpan<byte> haystack, int start)
    {
        int relative = haystack[start..].IndexOf(options.LineTerminator);
        return relative < 0 ? haystack.Length : start + relative;
    }

    private static bool TryGetRepeatedCapture(
        RegexSyntaxNode node,
        RegexSyntaxKind atomKind,
        out int captureIndex)
    {
        captureIndex = 0;
        if (UnwrapTransparentNonCapturingGroups(node) is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group ||
            UnwrapTransparentNonCapturingGroups(group.Child) is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: null,
                Lazy: false,
            } repetition ||
            UnwrapTransparentNonCapturingGroups(repetition.Child) is not RegexAtomNode { Kind: var actualKind } ||
            actualKind != atomKind)
        {
            return false;
        }

        captureIndex = group.CaptureIndex;
        return true;
    }

    private static bool TryCollectMarker(
        IReadOnlyList<RegexSyntaxNode> nodes,
        int start,
        int end,
        out byte[] marker)
    {
        marker = [];
        var bytes = new List<byte>();
        for (int index = start; index < end; index++)
        {
            if (UnwrapTransparentNonCapturingGroups(nodes[index]) is not RegexAtomNode
                {
                    Kind: RegexSyntaxKind.Literal,
                } literal)
            {
                return false;
            }

            bytes.AddRange(literal.Value.ToArray());
        }

        marker = bytes.ToArray();
        return true;
    }

    private static RegexSyntaxNode UnwrapTransparentNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.NonCapturingGroup,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group)
        {
            node = group.Child;
        }

        return node;
    }
}
