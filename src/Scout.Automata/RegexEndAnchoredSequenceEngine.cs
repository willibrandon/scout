namespace Scout;

internal sealed class RegexEndAnchoredSequenceEngine
{
    private const int MaxParts = 256;
    private readonly RegexEndAnchoredSequencePart[] parts;

    private RegexEndAnchoredSequenceEngine(RegexEndAnchoredSequencePart[] parts)
    {
        this.parts = parts;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexEndAnchoredSequenceEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.MultiLine ||
            options.Utf8 ||
            options.UnicodeClasses)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode sequence ||
            sequence.Nodes.Count < 3 ||
            !IsEndAnchor(sequence.Nodes[^1]))
        {
            return false;
        }

        var parts = new List<RegexEndAnchoredSequencePart>();
        bool sawRequiredLiteral = false;
        for (int index = 0; index < sequence.Nodes.Count - 1; index++)
        {
            if (!TryAppendPart(sequence.Nodes[index], options, parts, ref sawRequiredLiteral))
            {
                return false;
            }
        }

        if (parts.Count == 0 ||
            parts.Count > MaxParts ||
            !sawRequiredLiteral)
        {
            return false;
        }

        engine = new RegexEndAnchoredSequenceEngine(parts.ToArray());
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        if (!TryFindEndingAtHaystackEnd(haystack, out RegexMatch match))
        {
            return null;
        }

        if (match.Start >= lowerBound)
        {
            return match;
        }

        return TryMatchAt(haystack, lowerBound, out int length)
            ? new RegexMatch(lowerBound, length)
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return Find(haystack, startAt).HasValue ? 1 : 0;
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return Find(haystack, startAt) is RegexMatch match ? match.Length : 0;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start > (uint)haystack.Length ||
            !TryMatchForward(haystack, partIndex: 0, position: start))
        {
            return false;
        }

        length = haystack.Length - start;
        return true;
    }

    private bool TryFindEndingAtHaystackEnd(ReadOnlySpan<byte> haystack, out RegexMatch match)
    {
        int position = haystack.Length;
        for (int index = parts.Length - 1; index >= 0; index--)
        {
            RegexEndAnchoredSequencePart part = parts[index];
            if (part.Maximum.HasValue)
            {
                int repeat = part.Maximum.Value;
                for (int count = 0; count < repeat; count++)
                {
                    if (position <= 0 || !part.Segment.AtomMatches(haystack[position - 1]))
                    {
                        match = default;
                        return false;
                    }

                    position--;
                }

                continue;
            }

            int matched = 0;
            while (position > 0 && part.Segment.AtomMatches(haystack[position - 1]))
            {
                position--;
                matched++;
            }

            if (matched < part.Minimum)
            {
                match = default;
                return false;
            }
        }

        match = new RegexMatch(position, haystack.Length - position);
        return true;
    }

    private bool TryMatchForward(ReadOnlySpan<byte> haystack, int partIndex, int position)
    {
        if (partIndex == parts.Length)
        {
            return position == haystack.Length;
        }

        RegexEndAnchoredSequencePart part = parts[partIndex];
        if (part.Maximum.HasValue)
        {
            int repeat = part.Maximum.Value;
            for (int count = 0; count < repeat; count++)
            {
                if (position >= haystack.Length || !part.Segment.AtomMatches(haystack[position]))
                {
                    return false;
                }

                position++;
            }

            return TryMatchForward(haystack, partIndex + 1, position);
        }

        int start = position;
        while (position < haystack.Length && part.Segment.AtomMatches(haystack[position]))
        {
            position++;
        }

        for (int end = position; end >= start + part.Minimum; end--)
        {
            if (TryMatchForward(haystack, partIndex + 1, end))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAppendPart(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<RegexEndAnchoredSequencePart> parts,
        ref bool sawRequiredLiteral)
    {
        node = UnwrapTransparentGroups(node);
        if (node is RegexRepetitionNode repetition)
        {
            if (repetition.Lazy ||
                repetition.Maximum.HasValue && repetition.Maximum.Value != repetition.Minimum ||
                !TryCreateSegment(repetition.Child, options, out RegexSimpleSequenceSegment repeatedSegment))
            {
                return false;
            }

            parts.Add(new RegexEndAnchoredSequencePart(repeatedSegment, repetition.Minimum, repetition.Maximum));
            sawRequiredLiteral |= repeatedSegment.MatcherKind == RegexSimpleSequenceByteMatcherKind.Literal &&
                repetition.Minimum > 0;
            return true;
        }

        if (!TryCreateSegment(node, options, out RegexSimpleSequenceSegment segment))
        {
            return false;
        }

        parts.Add(new RegexEndAnchoredSequencePart(segment, Minimum: 1, Maximum: 1));
        sawRequiredLiteral |= segment.MatcherKind == RegexSimpleSequenceByteMatcherKind.Literal;
        return true;
    }

    private static bool TryCreateSegment(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSimpleSequenceSegment segment)
    {
        segment = default;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode atom ||
            IsPredicate(atom.Kind) ||
            atom.Kind == RegexSyntaxKind.Literal && atom.Value.Length != 1 ||
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
        return true;
    }

    private static bool IsEndAnchor(RegexSyntaxNode node)
    {
        return UnwrapTransparentGroups(node) is RegexAtomNode
        {
            Kind: RegexSyntaxKind.EndAnchor or RegexSyntaxKind.AbsoluteEndAnchor,
        };
    }

    private static bool IsPredicate(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.StartAnchor
            or RegexSyntaxKind.EndAnchor
            or RegexSyntaxKind.AbsoluteStartAnchor
            or RegexSyntaxKind.AbsoluteEndAnchor
            or RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary
            or RegexSyntaxKind.WordStartBoundary
            or RegexSyntaxKind.WordEndBoundary
            or RegexSyntaxKind.WordStartHalfBoundary
            or RegexSyntaxKind.WordEndHalfBoundary;
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
