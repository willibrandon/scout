namespace Scout;

internal sealed class SimpleReplacementCapturePlan
{
    private readonly byte[][] prefixes;
    private readonly int prefixCaptureIndex;
    private readonly int suffixCaptureIndex;
    private readonly bool[] suffixFirstBytes;
    private readonly bool[] suffixRestBytes;

    private SimpleReplacementCapturePlan(
        byte[][] prefixes,
        int prefixCaptureIndex,
        int suffixCaptureIndex,
        bool[] suffixFirstBytes,
        bool[] suffixRestBytes)
    {
        this.prefixes = prefixes;
        this.prefixCaptureIndex = prefixCaptureIndex;
        this.suffixCaptureIndex = suffixCaptureIndex;
        this.suffixFirstBytes = suffixFirstBytes;
        this.suffixRestBytes = suffixRestBytes;
    }

    public static bool TryCreate(
        ReadOnlySpan<byte> pattern,
        bool asciiCaseInsensitive,
        out SimpleReplacementCapturePlan? plan)
    {
        plan = null;
        if (asciiCaseInsensitive)
        {
            return false;
        }

        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        if (!TryGetConsumingItems(tree.Root, out List<RegexSyntaxNode> items) ||
            items.Count != 3 ||
            !TryGetCapturedLiteralAlternation(items[0], out byte[][] prefixes, out int prefixCaptureIndex) ||
            !TryGetWhitespaceRun(items[1]) ||
            !TryGetCapturedByteRun(items[2], out int suffixCaptureIndex, out bool[] suffixFirstBytes, out bool[] suffixRestBytes))
        {
            return false;
        }

        plan = new SimpleReplacementCapturePlan(
            prefixes,
            prefixCaptureIndex,
            suffixCaptureIndex,
            suffixFirstBytes,
            suffixRestBytes);
        return true;
    }

    public bool TryCollectNumericCaptures(
        ReadOnlySpan<byte> matched,
        int[] captureStarts,
        int[] captureLengths)
    {
        if (!TryMatch(matched, out int prefixLength, out int suffixStart, out int suffixLength))
        {
            return false;
        }

        Array.Fill(captureStarts, -1);
        Array.Fill(captureLengths, -1);
        SetCapture(captureStarts, captureLengths, 0, 0, matched.Length);
        SetCapture(captureStarts, captureLengths, prefixCaptureIndex, 0, prefixLength);
        SetCapture(captureStarts, captureLengths, suffixCaptureIndex, suffixStart, suffixLength);
        return true;
    }

    public bool TryAddExpandedNumericReplacement(
        List<byte> bytes,
        ReadOnlySpan<byte> matched,
        ReplacementTemplate template)
    {
        if (!TryMatch(matched, out int prefixLength, out int suffixStart, out int suffixLength))
        {
            return false;
        }

        template.AddExpanded(
            bytes,
            matched,
            prefixCaptureIndex,
            0,
            prefixLength,
            suffixCaptureIndex,
            suffixStart,
            suffixLength);
        return true;
    }

    public bool TryWriteExpandedNumericReplacement(
        RawByteWriter output,
        ReadOnlySpan<byte> matched,
        ReplacementTemplate template)
    {
        if (!TryMatch(matched, out int prefixLength, out int suffixStart, out int suffixLength))
        {
            return false;
        }

        template.WriteExpanded(
            output,
            matched,
            prefixCaptureIndex,
            0,
            prefixLength,
            suffixCaptureIndex,
            suffixStart,
            suffixLength);
        return true;
    }

    private bool TryMatch(
        ReadOnlySpan<byte> matched,
        out int prefixLength,
        out int suffixStart,
        out int suffixLength)
    {
        prefixLength = 0;
        suffixStart = 0;
        suffixLength = 0;
        for (int prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
        {
            ReadOnlySpan<byte> prefix = prefixes[prefixIndex];
            if (!matched.StartsWith(prefix))
            {
                continue;
            }

            int whitespaceStart = prefix.Length;
            int candidateSuffixStart = whitespaceStart;
            while (candidateSuffixStart < matched.Length && IsRegexWhitespaceByte(matched[candidateSuffixStart]))
            {
                candidateSuffixStart++;
            }

            if (candidateSuffixStart == whitespaceStart ||
                candidateSuffixStart >= matched.Length ||
                !suffixFirstBytes[matched[candidateSuffixStart]])
            {
                continue;
            }

            int suffixEnd = candidateSuffixStart + 1;
            while (suffixEnd < matched.Length && suffixRestBytes[matched[suffixEnd]])
            {
                suffixEnd++;
            }

            if (suffixEnd != matched.Length)
            {
                continue;
            }

            prefixLength = prefix.Length;
            suffixStart = candidateSuffixStart;
            suffixLength = suffixEnd - candidateSuffixStart;
            return true;
        }

        return false;
    }

    private static bool TryGetConsumingItems(RegexSyntaxNode root, out List<RegexSyntaxNode> items)
    {
        items = [];
        RegexSyntaxNode unwrapped = UnwrapTransparentNonCapturingGroups(root);
        if (unwrapped is RegexSequenceNode sequence)
        {
            for (int index = 0; index < sequence.Nodes.Count; index++)
            {
                RegexSyntaxNode node = UnwrapTransparentNonCapturingGroups(sequence.Nodes[index]);
                if (!IsZeroWidthAssertion(node))
                {
                    items.Add(node);
                }
            }

            return true;
        }

        if (!IsZeroWidthAssertion(unwrapped))
        {
            items.Add(unwrapped);
        }

        return true;
    }

    private static bool TryGetCapturedLiteralAlternation(
        RegexSyntaxNode node,
        out byte[][] prefixes,
        out int captureIndex)
    {
        prefixes = [];
        captureIndex = 0;
        if (UnwrapTransparentNonCapturingGroups(node) is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
            } group)
        {
            return false;
        }

        if (!HasTransparentCaptureFlags(group))
        {
            return false;
        }

        List<byte[]> literals = [];
        RegexSyntaxNode child = UnwrapTransparentNonCapturingGroups(group.Child);
        if (child is RegexAlternationNode alternation)
        {
            if (alternation.Alternatives.Count == 0)
            {
                return false;
            }

            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryGetLiteral(alternation.Alternatives[index], out byte[] literal))
                {
                    return false;
                }

                literals.Add(literal);
            }
        }
        else if (!TryGetLiteral(child, out byte[] literal))
        {
            return false;
        }
        else
        {
            literals.Add(literal);
        }

        if (literals.Count == 0)
        {
            return false;
        }

        prefixes = literals.ToArray();
        captureIndex = group.CaptureIndex;
        return true;
    }

    private static bool TryGetWhitespaceRun(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexRepetitionNode
        {
            Minimum: > 0,
            Maximum: null,
            Lazy: false,
            Child: RegexAtomNode { Kind: RegexSyntaxKind.WhitespaceClass },
        } ||
            node is RegexAtomNode { Kind: RegexSyntaxKind.WhitespaceClass };
    }

    private static bool TryGetCapturedByteRun(
        RegexSyntaxNode node,
        out int captureIndex,
        out bool[] firstBytes,
        out bool[] restBytes)
    {
        captureIndex = 0;
        firstBytes = [];
        restBytes = [];
        if (UnwrapTransparentNonCapturingGroups(node) is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
            } group)
        {
            return false;
        }

        if (!HasTransparentCaptureFlags(group))
        {
            return false;
        }

        RegexSyntaxNode child = UnwrapTransparentNonCapturingGroups(group.Child);
        if (child is RegexSequenceNode { Nodes.Count: 2 } sequence &&
            TryBuildByteLookup(sequence.Nodes[0], out firstBytes) &&
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[1]) is RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: null,
                Lazy: false,
            } repetition &&
            TryBuildByteLookup(repetition.Child, out restBytes))
        {
            captureIndex = group.CaptureIndex;
            return true;
        }

        if (child is RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: null,
                Lazy: false,
            } run &&
            TryBuildByteLookup(run.Child, out firstBytes))
        {
            restBytes = firstBytes;
            captureIndex = group.CaptureIndex;
            return true;
        }

        return false;
    }

    private static bool TryBuildByteLookup(RegexSyntaxNode node, out bool[] lookup)
    {
        lookup = new bool[256];
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexAtomNode { Kind: RegexSyntaxKind.WordClass })
        {
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                lookup[value] = IsAsciiWordByte((byte)value);
            }

            return true;
        }

        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom)
        {
            lookup = [];
            return false;
        }

        if (!TryBuildCharacterClassLookup(atom.Value.Span, lookup))
        {
            lookup = [];
            return false;
        }

        return true;
    }

    private static bool TryBuildCharacterClassLookup(ReadOnlySpan<byte> expression, bool[] lookup)
    {
        if (!expression.IsEmpty && expression[0] == (byte)'^')
        {
            return false;
        }

        int index = 0;
        while (index < expression.Length)
        {
            if (!TryReadClassByte(expression, ref index, out byte start))
            {
                return false;
            }

            if (index < expression.Length &&
                expression[index] == (byte)'-' &&
                index + 1 < expression.Length)
            {
                index++;
                if (!TryReadClassByte(expression, ref index, out byte end) ||
                    start > end)
                {
                    return false;
                }

                for (int value = start; value <= end; value++)
                {
                    lookup[value] = true;
                }

                continue;
            }

            lookup[start] = true;
        }

        return true;
    }

    private static bool TryReadClassByte(ReadOnlySpan<byte> expression, ref int index, out byte value)
    {
        value = 0;
        if (index >= expression.Length)
        {
            return false;
        }

        value = expression[index++];
        if (value != (byte)'\\')
        {
            return true;
        }

        if (index >= expression.Length)
        {
            return false;
        }

        value = expression[index++] switch
        {
            (byte)'n' => (byte)'\n',
            (byte)'r' => (byte)'\r',
            (byte)'t' => (byte)'\t',
            (byte)'f' => (byte)'\f',
            var escaped => escaped,
        };
        return true;
    }

    private static bool TryGetLiteral(RegexSyntaxNode node, out byte[] literal)
    {
        literal = [];
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
        {
            literal = atom.Value.ToArray();
            return literal.Length > 0;
        }

        if (node is not RegexSequenceNode sequence)
        {
            return false;
        }

        List<byte> bytes = [];
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = UnwrapTransparentNonCapturingGroups(sequence.Nodes[index]);
            if (child is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } childAtom)
            {
                return false;
            }

            bytes.AddRange(childAtom.Value.Span.ToArray());
        }

        literal = bytes.ToArray();
        return literal.Length > 0;
    }

    private static RegexSyntaxNode UnwrapTransparentNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.NonCapturingGroup,
            } group &&
            IsTransparentNonCapturingGroup(group))
        {
            node = group.Child;
        }

        return node;
    }

    private static bool IsTransparentNonCapturingGroup(RegexGroupNode group)
    {
        return group.EnabledFlags.Length == 0 &&
            (group.DisabledFlags.Length == 0 || group.DisabledFlags == "u");
    }

    private static bool HasTransparentCaptureFlags(RegexGroupNode group)
    {
        return group.EnabledFlags.Length == 0 &&
            (group.DisabledFlags.Length == 0 || group.DisabledFlags == "u");
    }

    private static void SetCapture(
        int[] starts,
        int[] lengths,
        int captureIndex,
        int start,
        int length)
    {
        if ((uint)captureIndex >= (uint)starts.Length)
        {
            return;
        }

        starts[captureIndex] = start;
        lengths[captureIndex] = length;
    }

    private static bool IsZeroWidthAssertion(RegexSyntaxNode node)
    {
        return node is RegexAtomNode
        {
            Kind: RegexSyntaxKind.WordBoundary
                or RegexSyntaxKind.NotWordBoundary
                or RegexSyntaxKind.WordStartBoundary
                or RegexSyntaxKind.WordEndBoundary
                or RegexSyntaxKind.WordStartHalfBoundary
                or RegexSyntaxKind.WordEndHalfBoundary
                or RegexSyntaxKind.StartAnchor
                or RegexSyntaxKind.EndAnchor
                or RegexSyntaxKind.AbsoluteStartAnchor
                or RegexSyntaxKind.AbsoluteEndAnchor,
        };
    }

    private static bool IsRegexWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }

    private static bool IsAsciiWordByte(byte value)
    {
        return value == (byte)'_' ||
            value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z';
    }
}
