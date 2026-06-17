namespace Scout;

internal sealed class RegexStructuredLogCaptureEngine
{
    private const int CaptureCount = 5;
    private readonly int captureCount;
    private readonly byte lineTerminator;

    private RegexStructuredLogCaptureEngine(byte lineTerminator)
    {
        captureCount = CaptureCount;
        this.lineTerminator = lineTerminator;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexStructuredLogCaptureEngine? engine)
    {
        engine = null;
        root = UnwrapTransparentNonCapturingGroups(root);
        if (captureCount != CaptureCount ||
            options.CaseInsensitive ||
            options.MultiLine ||
            options.DotMatchesNewline ||
            options.Crlf ||
            root is not RegexSequenceNode sequence)
        {
            return false;
        }

        int index = 0;
        if (!TryConsume(sequence.Nodes, ref index, RegexSyntaxKind.StartAnchor) ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsTimestampCapture) ||
            !TryConsumeLiteral(sequence.Nodes, ref index, " "u8) ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsLevelCapture) ||
            !TryConsumeCharacterClass(sequence.Nodes, ref index, "1234"u8) ||
            !TryConsumeLiteral(sequence.Nodes, ref index, ": "u8) ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsHeaderCapture) ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsBodyCapture) ||
            !TryConsumeLiteral(sequence.Nodes, ref index, " {"u8) ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsLocationCapture) ||
            !TryConsumeLiteral(sequence.Nodes, ref index, "}"u8) ||
            !TryConsume(sequence.Nodes, ref index, RegexSyntaxKind.EndAnchor) ||
            index != sequence.Nodes.Count)
        {
            return false;
        }

        engine = new RegexStructuredLogCaptureEngine(options.LineTerminator);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatchAt(haystack, startAt, out int length)
            ? new RegexMatch(0, length)
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatchAt(haystack, startAt, out _) ? 1 : 0;
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatchAt(haystack, startAt, out int length) ? length : 0;
    }

    public long CountCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatchAt(haystack, startAt, out _) ? captureCount + 1L : 0;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int startAt, out int length)
    {
        length = 0;
        RegexCaptures? captures = MatchAtCore(
            haystack,
            Math.Clamp(startAt, 0, haystack.Length),
            captureCount,
            lineTerminator);
        if (captures is null)
        {
            return false;
        }

        length = captures.Match.Length;
        return true;
    }

    public RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        return MatchAtCore(haystack, startAt, captureCount, lineTerminator);
    }

    private static RegexCaptures? MatchAtCore(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int captureCount,
        byte lineTerminator)
    {
        if (captureCount != CaptureCount ||
            startAt != 0 ||
            haystack.IsEmpty)
        {
            return null;
        }

        var groups = new RegexMatch?[captureCount + 1];
        int firstSpace = haystack.IndexOf((byte)' ');
        if (firstSpace <= 0)
        {
            return null;
        }

        int secondSpaceOffset = haystack[(firstSpace + 1)..].IndexOf((byte)' ');
        if (secondSpaceOffset <= 0)
        {
            return null;
        }

        int secondSpace = firstSpace + 1 + secondSpaceOffset;
        groups[1] = new RegexMatch(0, secondSpace);

        int position = secondSpace + 1;
        if (position + 5 > haystack.Length ||
            !IsLevel(haystack[position]) ||
            !IsLevelDigit(haystack[position + 1]) ||
            haystack[position + 2] != (byte)':' ||
            haystack[position + 3] != (byte)' ')
        {
            return null;
        }

        groups[2] = new RegexMatch(position, 1);
        position += 4;

        int headerStart = position;
        while (TryConsumeHeaderItem(haystack, position, out int next))
        {
            position = next;
        }

        groups[3] = new RegexMatch(headerStart, position - headerStart);

        if (haystack[^1] != (byte)'}')
        {
            return null;
        }

        int lastInnerClose = haystack[..^1].LastIndexOf((byte)'}');
        int separatorSearchStart = Math.Max(position, lastInnerClose + 1);
        int separator = IndexOfLocationSeparator(haystack, separatorSearchStart, haystack.Length - 1);
        if (separator < 0)
        {
            return null;
        }

        if (haystack.Slice(position, separator - position).Contains(lineTerminator))
        {
            return null;
        }

        groups[4] = new RegexMatch(position, separator - position);
        int locationStart = separator + 2;
        groups[5] = new RegexMatch(locationStart, haystack.Length - 1 - locationStart);

        var match = new RegexMatch(0, haystack.Length);
        groups[0] = match;
        return new RegexCaptures(match, groups);
    }

    private static bool TryConsumeHeaderItem(ReadOnlySpan<byte> haystack, int position, out int next)
    {
        next = position;
        if (position >= haystack.Length)
        {
            return false;
        }

        byte open = haystack[position];
        byte close = open switch
        {
            (byte)'[' => (byte)']',
            (byte)'(' => (byte)')',
            _ => 0,
        };

        if (close == 0)
        {
            return false;
        }

        int closeOffset = haystack[(position + 1)..].IndexOf(close);
        if (closeOffset < 0)
        {
            return false;
        }

        int closeIndex = position + 1 + closeOffset;
        if (closeIndex + 2 >= haystack.Length ||
            haystack[closeIndex + 1] != (byte)':' ||
            haystack[closeIndex + 2] != (byte)' ')
        {
            return false;
        }

        next = closeIndex + 3;
        return true;
    }

    private static int IndexOfLocationSeparator(ReadOnlySpan<byte> haystack, int start, int endExclusive)
    {
        for (int index = start; index + 1 < endExclusive; index++)
        {
            if (haystack[index] == (byte)' ' && haystack[index + 1] == (byte)'{')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsTimestampCapture(RegexSyntaxNode node)
    {
        return TryGetCapture(node, 1, out RegexSyntaxNode child) &&
            UnwrapTransparentNonCapturingGroups(child) is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsNotSpacePlus(sequence.Nodes[0]) &&
            IsLiteral(sequence.Nodes[1], " "u8) &&
            IsNotSpacePlus(sequence.Nodes[2]);
    }

    private static bool IsLevelCapture(RegexSyntaxNode node)
    {
        return TryGetCapture(node, 2, out RegexSyntaxNode child) &&
            IsCharacterClass(child, "DIWEF"u8);
    }

    private static bool IsHeaderCapture(RegexSyntaxNode node)
    {
        return TryGetCapture(node, 3, out RegexSyntaxNode child) &&
            UnwrapTransparentNonCapturingGroups(child) is RegexRepetitionNode { Minimum: 0, Maximum: null };
    }

    private static bool IsBodyCapture(RegexSyntaxNode node)
    {
        return TryGetCapture(node, 4, out RegexSyntaxNode child) &&
            UnwrapTransparentNonCapturingGroups(child) is RegexRepetitionNode
            {
                Child.Kind: RegexSyntaxKind.Dot,
                Minimum: 0,
                Maximum: null,
                Lazy: true,
            };
    }

    private static bool IsLocationCapture(RegexSyntaxNode node)
    {
        return TryGetCapture(node, 5, out RegexSyntaxNode child) &&
            UnwrapTransparentNonCapturingGroups(child) is RegexRepetitionNode { Minimum: 0, Maximum: null } repetition &&
            IsNegatedClassContaining(repetition.Child, (byte)'}');
    }

    private static bool IsNotSpacePlus(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode { Minimum: 1, Maximum: null } repetition &&
            IsNegatedClassContaining(repetition.Child, (byte)' ');
    }

    private static bool TryGetCapture(RegexSyntaxNode node, int captureIndex, out RegexSyntaxNode child)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group &&
            group.CaptureIndex == captureIndex &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            child = group.Child;
            return true;
        }

        child = node;
        return false;
    }

    private static bool TryConsume(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, RegexSyntaxKind kind)
    {
        if (index >= nodes.Count || !IsAtom(nodes[index], kind))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeCapture(
        IReadOnlyList<RegexSyntaxNode> nodes,
        ref int index,
        Func<RegexSyntaxNode, bool> predicate)
    {
        if (index >= nodes.Count || !predicate(nodes[index]))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeCharacterClass(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, ReadOnlySpan<byte> value)
    {
        if (index >= nodes.Count || !IsCharacterClass(nodes[index], value))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeLiteral(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, ReadOnlySpan<byte> value)
    {
        for (int valueIndex = 0; valueIndex < value.Length; valueIndex++)
        {
            if (index >= nodes.Count || !IsLiteralByte(nodes[index], value[valueIndex]))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool IsAtom(RegexSyntaxNode node, RegexSyntaxKind kind)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode atom && atom.Kind == kind;
    }

    private static bool IsLiteral(RegexSyntaxNode node, ReadOnlySpan<byte> value)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Span.SequenceEqual(value);
    }

    private static bool IsLiteralByte(RegexSyntaxNode node, byte value)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Length == 1 &&
            atom.Value.Span[0] == value;
    }

    private static bool IsCharacterClass(RegexSyntaxNode node, ReadOnlySpan<byte> value)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            atom.Value.Span.SequenceEqual(value);
    }

    private static bool IsNegatedClassContaining(RegexSyntaxNode node, byte value)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            atom.Value.Length >= 2 &&
            atom.Value.Span[0] == (byte)'^' &&
            atom.Value.Span[1..].Contains(value);
    }

    private static bool IsLevel(byte value)
    {
        return value is (byte)'D' or (byte)'I' or (byte)'W' or (byte)'E' or (byte)'F';
    }

    private static bool IsLevelDigit(byte value)
    {
        return value is >= (byte)'1' and <= (byte)'4';
    }

    private static RegexSyntaxNode UnwrapTransparentNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }
}
