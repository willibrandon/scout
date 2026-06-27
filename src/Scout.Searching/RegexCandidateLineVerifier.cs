namespace Scout;

internal sealed class RegexCandidateLineVerifier
{
    private const int MaxPrefixes = 16;
    private const int MaxSegments = 12;

    private readonly byte[][] prefixes;
    private readonly int[][] prefixIndexesByFirstByte;
    private readonly RegexCandidateLineVerifierSegment[] segments;
    private readonly bool leadingWordBoundary;

    private RegexCandidateLineVerifier(
        byte[][] prefixes,
        RegexCandidateLineVerifierSegment[] segments,
        bool leadingWordBoundary)
    {
        this.prefixes = prefixes;
        this.segments = segments;
        this.leadingWordBoundary = leadingWordBoundary;
        prefixIndexesByFirstByte = BuildPrefixBuckets(prefixes);
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

        var segments = new List<RegexCandidateLineVerifierSegment>();
        while (index < items.Count)
        {
            if (!TryConsumeSegment(items[index].Node, items[index].Options, out RegexCandidateLineVerifierSegment segment))
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

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length, out bool completed)
    {
        length = 0;
        completed = true;
        if ((uint)start > (uint)haystack.Length)
        {
            return false;
        }

        if (leadingWordBoundary &&
            (!TryAsciiWordBoundary(haystack, start, out bool boundaryMatches, out completed) || !boundaryMatches))
        {
            return false;
        }

        ReadOnlySpan<int> prefixIndexes = start < haystack.Length
            ? prefixIndexesByFirstByte[haystack[start]]
            : [];
        for (int index = 0; index < prefixIndexes.Length; index++)
        {
            byte[] prefix = prefixes[prefixIndexes[index]];
            if (PrefixMatches(haystack, start, prefix) &&
                TryMatchSegments(haystack, start + prefix.Length, out int end, out completed))
            {
                length = end - start;
                return true;
            }

            if (!completed)
            {
                return false;
            }
        }

        return false;
    }

    private bool TryMatchSegments(ReadOnlySpan<byte> haystack, int position, out int end, out bool completed)
    {
        completed = true;
        for (int index = 0; index < segments.Length; index++)
        {
            RegexCandidateLineVerifierSegment segment = segments[index];
            int count = 0;
            while (count < segment.Minimum)
            {
                if (position >= haystack.Length ||
                    !segment.TryAtomMatches(haystack[position], out bool matches, out completed) ||
                    !matches)
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
                segment.TryAtomMatches(haystack[position], out bool matches, out completed) &&
                matches)
            {
                position++;
                count++;
            }

            if (!completed)
            {
                end = 0;
                return false;
            }
        }

        end = position;
        return true;
    }

    private static bool TryAsciiWordBoundary(ReadOnlySpan<byte> haystack, int position, out bool matches, out bool completed)
    {
        completed = true;
        byte left = position > 0 ? haystack[position - 1] : (byte)0;
        byte right = position < haystack.Length ? haystack[position] : (byte)0;
        if ((position > 0 && left > 0x7F) ||
            (position < haystack.Length && right > 0x7F))
        {
            matches = false;
            completed = false;
            return false;
        }

        bool leftIsWord = position > 0 && RegexSimpleSequenceSegment.IsAsciiWord(left);
        bool rightIsWord = position < haystack.Length && RegexSimpleSequenceSegment.IsAsciiWord(right);
        matches = leftIsWord != rightIsWord;
        return true;
    }

    private static bool PrefixMatches(ReadOnlySpan<byte> haystack, int start, byte[] prefix)
    {
        if (prefix.Length > haystack.Length - start)
        {
            return false;
        }

        if (prefix.Length >= 2 && haystack[start + 1] != prefix[1])
        {
            return false;
        }

        return haystack.Slice(start, prefix.Length).SequenceEqual(prefix);
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

        return TryAppendSequenceItems(sequence, effectiveOptions, items);
    }

    private static bool TryAppendSequenceItems(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items)
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

            if (!TryUnwrapWithOptions(child, currentOptions, out RegexSyntaxNode unwrapped, out RegexCompileOptions childOptions))
            {
                return false;
            }

            if (unwrapped is RegexSequenceNode childSequence)
            {
                if (!TryAppendSequenceItems(childSequence, childOptions, items))
                {
                    return false;
                }

                continue;
            }

            items.Add((unwrapped, childOptions));
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
        out RegexCandidateLineVerifierSegment segment)
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

        byte[] value = atom.Value.ToArray();
        var simple = new RegexSimpleSequenceSegment(
            atom.Kind,
            value,
            effectiveOptions.CaseInsensitive,
            effectiveOptions.MultiLine,
            effectiveOptions.DotMatchesNewline,
            effectiveOptions.Crlf,
            effectiveOptions.LineTerminator,
            minimum,
            maximum,
            lazy);
        bool requiresUtf8ScalarFallback = RegexByteClass.RequiresUtf8ScalarMatch(
            atom.Kind,
            value,
            effectiveOptions.Utf8,
            effectiveOptions.CaseInsensitive,
            effectiveOptions.UnicodeClasses);
        segment = new RegexCandidateLineVerifierSegment(simple, requiresUtf8ScalarFallback);
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

    private static int[][] BuildPrefixBuckets(byte[][] prefixes)
    {
        var buckets = new List<int>[256];
        for (int index = 0; index < prefixes.Length; index++)
        {
            byte[] prefix = prefixes[index];
            if (prefix.Length == 0)
            {
                continue;
            }

            byte first = prefix[0];
            buckets[first] ??= [];
            buckets[first]!.Add(index);
        }

        int[][] result = new int[256][];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = buckets[index]?.ToArray() ?? [];
        }

        return result;
    }

}
