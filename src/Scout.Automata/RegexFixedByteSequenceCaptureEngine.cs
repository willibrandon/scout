namespace Scout;

internal sealed class RegexFixedByteSequenceCaptureEngine
{
    private const int MaxExpandedSegmentLength = 64;

    private readonly RegexFixedByteSequenceCaptureSegment[] segments;
    private readonly int captureCount;
    private readonly int minimumLength;

    private RegexFixedByteSequenceCaptureEngine(
        RegexFixedByteSequenceCaptureSegment[] segments,
        int captureCount,
        int minimumLength)
    {
        this.segments = segments;
        this.captureCount = captureCount;
        this.minimumLength = minimumLength;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexFixedByteSequenceCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 ||
            !TryCollectItems(root, options, out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items) ||
            items.Count == 0)
        {
            return false;
        }

        List<RegexFixedByteSequenceCaptureSegment> segments = [];
        bool sawOptional = false;
        int mandatorySegmentCount = 0;
        int minimumLength = 0;
        bool[] seenCaptures = new bool[captureCount + 1];
        for (int index = 0; index < items.Count; index++)
        {
            if (!TryGetSegment(items[index].Node, items[index].Options, out RegexFixedByteSequenceCaptureSegment segment) ||
                segment.CaptureIndex > captureCount ||
                seenCaptures[segment.CaptureIndex])
            {
                return false;
            }

            seenCaptures[segment.CaptureIndex] = true;
            if (segment.Optional)
            {
                sawOptional = true;
            }
            else
            {
                if (sawOptional)
                {
                    return false;
                }

                mandatorySegmentCount++;
                minimumLength += segment.Length;
            }

            segments.Add(segment);
        }

        if (mandatorySegmentCount == 0 ||
            segments.Count != captureCount)
        {
            return false;
        }

        engine = new RegexFixedByteSequenceCaptureEngine(segments.ToArray(), captureCount, minimumLength);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int lastStart = haystack.Length - minimumLength;
        while (start <= lastStart)
        {
            if (TryMatchAt(haystack, start, groups: null, out _, out _))
            {
                return CreateCapturesAt(haystack, start);
            }

            start++;
        }

        return null;
    }

    private RegexCaptures CreateCapturesAt(ReadOnlySpan<byte> haystack, int start)
    {
        var groups = new RegexMatch?[captureCount + 1];
        _ = TryMatchAt(haystack, start, groups, out RegexMatch match, out _);
        return new RegexCaptures(match, groups);
    }

    public long CountCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int lastStart = haystack.Length - minimumLength;
        while (offset <= lastStart)
        {
            if (TryMatchAt(haystack, offset, groups: null, out RegexMatch match, out int participatingCount))
            {
                total += participatingCount;
                offset = match.End;
            }
            else
            {
                offset++;
            }
        }

        return total;
    }

    private bool TryMatchAt(
        ReadOnlySpan<byte> haystack,
        int start,
        RegexMatch?[]? groups,
        out RegexMatch match,
        out int participatingCount)
    {
        int position = start;
        participatingCount = 1;
        for (int index = 0; index < segments.Length; index++)
        {
            RegexFixedByteSequenceCaptureSegment segment = segments[index];
            if (!segment.Matches(haystack, position))
            {
                if (segment.Optional)
                {
                    continue;
                }

                match = default;
                participatingCount = 0;
                return false;
            }

            groups?[segment.CaptureIndex] = new RegexMatch(position, segment.Length);
            position += segment.Length;
            participatingCount++;
        }

        match = new RegexMatch(start, position - start);
        if (groups is not null)
        {
            groups[0] = match;
        }

        return true;
    }

    private static bool TryGetSegment(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexFixedByteSequenceCaptureSegment segment)
    {
        segment = default;
        bool optional = false;
        if (!TryUnwrapTransparentNonCapturingWithOptions(node, options, out node, out options))
        {
            return false;
        }

        if (node is RegexRepetitionNode { Minimum: 0, Maximum: 1 } repetition)
        {
            bool lazy = options.SwapGreed ? !repetition.Lazy : repetition.Lazy;
            if (lazy)
            {
                return false;
            }

            optional = true;
            node = repetition.Child;
            if (!TryUnwrapTransparentNonCapturingWithOptions(node, options, out node, out options))
            {
                return false;
            }
        }

        if (node is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
            } group)
        {
            return false;
        }

        RegexCompileOptions groupOptions = options.Apply(group.EnabledFlags, group.DisabledFlags);
        List<RegexSimpleSequenceSegment> atoms = [];
        if (!TryCollectFixedByteAtoms(group.Child, groupOptions, atoms) ||
            atoms.Count == 0)
        {
            return false;
        }

        segment = new RegexFixedByteSequenceCaptureSegment(group.CaptureIndex, atoms.ToArray(), optional);
        return true;
    }

    private static bool TryCollectFixedByteAtoms(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<RegexSimpleSequenceSegment> atoms)
    {
        if (!TryUnwrapTransparentNonCapturingWithOptions(node, options, out node, out options))
        {
            return false;
        }

        if (node is RegexSequenceNode sequence)
        {
            RegexCompileOptions currentOptions = options;
            for (int index = 0; index < sequence.Nodes.Count; index++)
            {
                RegexSyntaxNode child = sequence.Nodes[index];
                if (child is RegexInlineFlagsNode flags)
                {
                    currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                    continue;
                }

                if (!TryCollectFixedByteAtoms(child, currentOptions, atoms))
                {
                    return false;
                }
            }

            return true;
        }

        if (node is RegexRepetitionNode { Minimum: > 0, Maximum: { } maximum } repetition)
        {
            if (repetition.Minimum != maximum ||
                maximum > MaxExpandedSegmentLength ||
                atoms.Count + maximum > MaxExpandedSegmentLength)
            {
                return false;
            }

            List<RegexSimpleSequenceSegment> repeated = [];
            if (!TryCollectFixedByteAtoms(repetition.Child, options, repeated) ||
                repeated.Count == 0 ||
                atoms.Count + repeated.Count * maximum > MaxExpandedSegmentLength)
            {
                return false;
            }

            for (int repeat = 0; repeat < maximum; repeat++)
            {
                atoms.AddRange(repeated);
            }

            return true;
        }

        if (node is not RegexAtomNode atom ||
            !IsByteConsumingAtom(atom.Kind) ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        if (atom.Kind == RegexSyntaxKind.Literal && atom.Value.Length != 1)
        {
            return false;
        }

        atoms.Add(new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            minimum: 1,
            maximum: 1,
            lazy: false));
        return true;
    }

    private static bool TryCollectItems(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items)
    {
        items = [];
        if (!TryUnwrapTransparentNonCapturingWithOptions(root, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions))
        {
            return false;
        }

        if (unwrapped is not RegexSequenceNode sequence)
        {
            items.Add((unwrapped, effectiveOptions));
            return true;
        }

        RegexCompileOptions currentOptions = effectiveOptions;
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

    private static bool TryUnwrapTransparentNonCapturingWithOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSyntaxNode unwrapped,
        out RegexCompileOptions effectiveOptions)
    {
        while (node is RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group)
        {
            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            node = group.Child;
        }

        unwrapped = node;
        effectiveOptions = options;
        return true;
    }

    private static bool IsByteConsumingAtom(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.Literal
            or RegexSyntaxKind.Dot
            or RegexSyntaxKind.AnyClass
            or RegexSyntaxKind.CharacterClass
            or RegexSyntaxKind.ByteClass
            or RegexSyntaxKind.DigitClass
            or RegexSyntaxKind.NotDigitClass
            or RegexSyntaxKind.WordClass
            or RegexSyntaxKind.NotWordClass
            or RegexSyntaxKind.WhitespaceClass
            or RegexSyntaxKind.NotWhitespaceClass;
    }

}
