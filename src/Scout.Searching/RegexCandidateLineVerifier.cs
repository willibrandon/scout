namespace Scout;

internal sealed class RegexCandidateLineVerifier
{
    private const int MaxPrefixes = 16;
    private const int MaxSegments = 12;

    private readonly byte[][] prefixes;
    private readonly RegexSimpleSequenceSegment[] segments;
    private readonly bool leadingWordBoundary;

    private RegexCandidateLineVerifier(
        byte[][] prefixes,
        RegexSimpleSequenceSegment[] segments,
        bool leadingWordBoundary)
    {
        this.prefixes = prefixes;
        this.segments = segments;
        this.leadingWordBoundary = leadingWordBoundary;
    }

    public static bool TryCompile(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexCandidateLineVerifier? verifier)
    {
        verifier = null;
        if (options.CaseInsensitive ||
            options.Crlf ||
            options.DotMatchesNewline)
        {
            return false;
        }

        if (!TryCollectSequenceItems(root, options, out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items))
        {
            return false;
        }

        int index = 0;
        bool leadingWordBoundary = TryConsumePredicate(items, ref index, RegexSyntaxKind.WordBoundary);
        if (!TryConsumeLiteralPrefixes(items, ref index, out byte[][] prefixes) ||
            prefixes.Length == 0 ||
            prefixes.Length > MaxPrefixes)
        {
            return false;
        }

        var segments = new List<RegexSimpleSequenceSegment>();
        while (index < items.Count)
        {
            if (!TryConsumeSegment(items[index].Node, items[index].Options, out RegexSimpleSequenceSegment segment))
            {
                return false;
            }

            segments.Add(segment);
            if (segments.Count > MaxSegments)
            {
                return false;
            }

            index++;
        }

        if (segments.Count == 0)
        {
            return false;
        }

        verifier = new RegexCandidateLineVerifier(prefixes, segments.ToArray(), leadingWordBoundary);
        return true;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start > (uint)haystack.Length ||
            leadingWordBoundary && !RegexByteClass.PredicateMatches(
                haystack,
                start,
                RegexSyntaxKind.WordBoundary,
                multiLine: false,
                crlf: false,
                lineTerminator: (byte)'\n',
                utf8: false,
                unicodeClasses: false))
        {
            return false;
        }

        for (int index = 0; index < prefixes.Length; index++)
        {
            byte[] prefix = prefixes[index];
            if (prefix.Length <= haystack.Length - start &&
                haystack.Slice(start, prefix.Length).SequenceEqual(prefix) &&
                TryMatchSegments(haystack, start + prefix.Length, out int end))
            {
                length = end - start;
                return true;
            }
        }

        return false;
    }

    private bool TryMatchSegments(ReadOnlySpan<byte> haystack, int position, out int end)
    {
        for (int index = 0; index < segments.Length; index++)
        {
            RegexSimpleSequenceSegment segment = segments[index];
            int count = 0;
            while (count < segment.Minimum)
            {
                if (position >= haystack.Length || !segment.AtomMatches(haystack[position]))
                {
                    end = 0;
                    return false;
                }

                position++;
                count++;
            }

            int maximum = segment.Maximum ?? int.MaxValue;
            while (count < maximum &&
                position < haystack.Length &&
                segment.AtomMatches(haystack[position]))
            {
                position++;
                count++;
            }
        }

        end = position;
        return true;
    }

    private static bool TryCollectSequenceItems(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items)
    {
        items = [];
        if (!TryUnwrapWithOptions(root, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions))
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

    private static bool TryConsumePredicate(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index,
        RegexSyntaxKind kind)
    {
        if (index >= items.Count ||
            !TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            effectiveOptions.CaseInsensitive ||
            unwrapped is not RegexAtomNode atom ||
            atom.Kind != kind)
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeLiteralPrefixes(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index,
        out byte[][] prefixes)
    {
        prefixes = [];
        if (index >= items.Count ||
            !TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            effectiveOptions.CaseInsensitive)
        {
            return false;
        }

        if (TryCollectLiteralAlternatives(unwrapped, out prefixes))
        {
            index++;
            return true;
        }

        if (TryCollectLiteralRun(items, ref index, out byte[] literal))
        {
            prefixes = [literal];
            return true;
        }

        return false;
    }

    private static bool TryCollectLiteralAlternatives(RegexSyntaxNode node, out byte[][] literals)
    {
        literals = [];
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAlternationNode alternation || alternation.Alternatives.Count == 0)
        {
            return false;
        }

        literals = new byte[alternation.Alternatives.Count][];
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryCollectLiteralNode(alternation.Alternatives[index], out byte[] literal) || literal.Length == 0)
            {
                literals = [];
                return false;
            }

            literals[index] = literal;
        }

        return true;
    }

    private static bool TryCollectLiteralRun(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index,
        out byte[] literal)
    {
        literal = [];
        var bytes = new List<byte>();
        while (index < items.Count &&
            !items[index].Options.CaseInsensitive &&
            TryCollectLiteralNode(items[index].Node, out byte[] nodeLiteral))
        {
            bytes.AddRange(nodeLiteral);
            index++;
        }

        literal = bytes.ToArray();
        return literal.Length > 0;
    }

    private static bool TryCollectLiteralNode(RegexSyntaxNode node, out byte[] literal)
    {
        literal = [];
        node = UnwrapTransparentGroups(node);
        if (node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
        {
            literal = atom.Value.ToArray();
            return literal.Length > 0;
        }

        if (node is not RegexSequenceNode sequence)
        {
            return false;
        }

        var bytes = new List<byte>();
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            if (sequence.Nodes[index] is RegexInlineFlagsNode)
            {
                return false;
            }

            RegexSyntaxNode child = UnwrapTransparentGroups(sequence.Nodes[index]);
            if (child is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } childAtom)
            {
                return false;
            }

            bytes.AddRange(childAtom.Value.ToArray());
        }

        literal = bytes.ToArray();
        return literal.Length > 0;
    }

    private static bool TryConsumeSegment(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSimpleSequenceSegment segment)
    {
        segment = default;
        if (!TryUnwrapWithOptions(node, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            effectiveOptions.CaseInsensitive ||
            effectiveOptions.Crlf ||
            effectiveOptions.DotMatchesNewline)
        {
            return false;
        }

        int minimum = 1;
        int? maximum = 1;
        bool lazy = false;
        if (unwrapped is RegexRepetitionNode repetition)
        {
            minimum = repetition.Minimum;
            maximum = repetition.Maximum;
            lazy = repetition.Lazy;
            if (!TryUnwrapWithOptions(repetition.Child, effectiveOptions, out unwrapped, out effectiveOptions) ||
                effectiveOptions.CaseInsensitive ||
                effectiveOptions.Crlf ||
                effectiveOptions.DotMatchesNewline)
            {
                return false;
            }
        }

        if (unwrapped is not RegexAtomNode atom ||
            atom.Kind is not (RegexSyntaxKind.Literal
                or RegexSyntaxKind.CharacterClass
                or RegexSyntaxKind.ByteClass
                or RegexSyntaxKind.DigitClass
                or RegexSyntaxKind.NotDigitClass
                or RegexSyntaxKind.WordClass
                or RegexSyntaxKind.NotWordClass
                or RegexSyntaxKind.WhitespaceClass
                or RegexSyntaxKind.NotWhitespaceClass
                or RegexSyntaxKind.Dot
                or RegexSyntaxKind.AnyClass))
        {
            return false;
        }

        segment = new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            effectiveOptions.CaseInsensitive,
            effectiveOptions.MultiLine,
            effectiveOptions.DotMatchesNewline,
            effectiveOptions.Crlf,
            effectiveOptions.LineTerminator,
            minimum,
            maximum,
            lazy);
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

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode group)
        {
            node = group.Child;
        }

        return node;
    }
}
