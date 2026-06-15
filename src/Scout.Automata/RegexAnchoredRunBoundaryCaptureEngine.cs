namespace Scout;

internal sealed class RegexAnchoredRunBoundaryCaptureEngine
{
    private readonly RegexAnchoredRunBoundaryCaptureAtom runAtom;
    private readonly RegexAnchoredRunBoundaryCaptureAtom suffixAtom;
    private readonly RegexCompileOptions options;
    private readonly int runLength;
    private readonly int runCaptureIndex;
    private readonly int suffixCaptureIndex;
    private readonly int captureCount;

    private RegexAnchoredRunBoundaryCaptureEngine(
        RegexAnchoredRunBoundaryCaptureAtom runAtom,
        RegexAnchoredRunBoundaryCaptureAtom suffixAtom,
        RegexCompileOptions options,
        int runLength,
        int runCaptureIndex,
        int suffixCaptureIndex,
        int captureCount)
    {
        this.runAtom = runAtom;
        this.suffixAtom = suffixAtom;
        this.options = options;
        this.runLength = runLength;
        this.runCaptureIndex = runCaptureIndex;
        this.suffixCaptureIndex = suffixCaptureIndex;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexAnchoredRunBoundaryCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 || options.MultiLine)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 4 } sequence ||
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[0]) is not RegexAtomNode { Kind: RegexSyntaxKind.StartAnchor } ||
            !TryGetFixedRunCapture(sequence.Nodes[1], out RegexAnchoredRunBoundaryCaptureAtom runAtom, out int runLength, out int runCaptureIndex) ||
            !TryGetSingleAtomCapture(sequence.Nodes[2], out RegexAnchoredRunBoundaryCaptureAtom suffixAtom, out int suffixCaptureIndex) ||
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[3]) is not RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary })
        {
            return false;
        }

        engine = new RegexAnchoredRunBoundaryCaptureEngine(
            runAtom,
            suffixAtom,
            options,
            runLength,
            runCaptureIndex,
            suffixCaptureIndex,
            captureCount);
        return true;
    }

    public RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (startAt != 0)
        {
            return null;
        }

        int position = 0;
        for (int count = 0; count < runLength; count++)
        {
            if (!TryConsume(runAtom, haystack, position, out int length))
            {
                return null;
            }

            position += length;
        }

        int suffixStart = position;
        if (!TryConsume(suffixAtom, haystack, position, out int suffixLength))
        {
            return null;
        }

        position += suffixLength;
        if (!RegexByteClass.PredicateMatches(
                haystack,
                position,
                RegexSyntaxKind.WordBoundary,
                options.MultiLine,
                options.Crlf,
                options.LineTerminator,
                options.Utf8,
                options.UnicodeClasses))
        {
            return null;
        }

        var match = new RegexMatch(0, position);
        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match;
        groups[runCaptureIndex] = new RegexMatch(0, suffixStart);
        groups[suffixCaptureIndex] = new RegexMatch(suffixStart, suffixLength);
        return new RegexCaptures(match, groups);
    }

    private bool TryConsume(
        RegexAnchoredRunBoundaryCaptureAtom atom,
        ReadOnlySpan<byte> haystack,
        int position,
        out int length)
    {
        if (atom.Kind == RegexSyntaxKind.NotWhitespaceClass &&
            TryConsumeNotWhitespace(haystack, position, out length))
        {
            return true;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            atom.Kind,
            atom.Value,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out length);
    }

    private bool TryConsumeNotWhitespace(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        if ((uint)position >= (uint)haystack.Length)
        {
            return false;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            if (RegexSimpleSequenceSegment.IsRegexWhitespace(first))
            {
                return false;
            }

            length = 1;
            return true;
        }

        if (!options.Utf8 && !options.UnicodeClasses)
        {
            length = 1;
            return true;
        }

        if ((first == 0xD0 || first == 0xD1) &&
            position + 1 < haystack.Length &&
            haystack[position + 1] is >= 0x80 and <= 0xBF)
        {
            length = 2;
            return true;
        }

        return false;
    }

    private static bool TryGetFixedRunCapture(
        RegexSyntaxNode node,
        out RegexAnchoredRunBoundaryCaptureAtom atom,
        out int length,
        out int captureIndex)
    {
        atom = default;
        length = 0;
        captureIndex = 0;
        if (!TryGetCaptureChild(node, out RegexSyntaxNode child, out captureIndex) ||
            UnwrapTransparentNonCapturingGroups(child) is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: { } maximum,
                Lazy: false,
            } repetition ||
            repetition.Minimum != maximum ||
            UnwrapTransparentNonCapturingGroups(repetition.Child) is not RegexAtomNode atomNode ||
            !TryCreateAtom(atomNode, out atom))
        {
            return false;
        }

        length = repetition.Minimum;
        return true;
    }

    private static bool TryGetSingleAtomCapture(
        RegexSyntaxNode node,
        out RegexAnchoredRunBoundaryCaptureAtom atom,
        out int captureIndex)
    {
        atom = default;
        captureIndex = 0;
        return TryGetCaptureChild(node, out RegexSyntaxNode child, out captureIndex) &&
            UnwrapTransparentNonCapturingGroups(child) is RegexAtomNode atomNode &&
            TryCreateAtom(atomNode, out atom);
    }

    private static bool TryGetCaptureChild(RegexSyntaxNode node, out RegexSyntaxNode child, out int captureIndex)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group)
        {
            child = group.Child;
            captureIndex = group.CaptureIndex;
            return true;
        }

        child = node;
        captureIndex = 0;
        return false;
    }

    private static bool TryCreateAtom(
        RegexAtomNode atom,
        out RegexAnchoredRunBoundaryCaptureAtom captureAtom)
    {
        captureAtom = default;
        if (atom.Kind is RegexSyntaxKind.StartAnchor
            or RegexSyntaxKind.EndAnchor
            or RegexSyntaxKind.AbsoluteStartAnchor
            or RegexSyntaxKind.AbsoluteEndAnchor
            or RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary
            or RegexSyntaxKind.WordStartBoundary
            or RegexSyntaxKind.WordEndBoundary
            or RegexSyntaxKind.WordStartHalfBoundary
            or RegexSyntaxKind.WordEndHalfBoundary)
        {
            return false;
        }

        captureAtom = new RegexAnchoredRunBoundaryCaptureAtom(atom.Kind, atom.Value.ToArray());
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
