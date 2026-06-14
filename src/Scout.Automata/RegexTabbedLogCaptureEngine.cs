namespace Scout;

internal sealed class RegexTabbedLogCaptureEngine
{
    private const int CaptureCount = 5;

    private readonly byte lineTerminator;

    private RegexTabbedLogCaptureEngine(byte lineTerminator)
    {
        this.lineTerminator = lineTerminator;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexTabbedLogCaptureEngine? engine)
    {
        engine = null;
        root = UnwrapTransparentNonCapturingGroups(root);
        if (captureCount != CaptureCount ||
            options.CaseInsensitive ||
            options.MultiLine ||
            options.DotMatchesNewline ||
            options.Crlf ||
            options.Utf8 ||
            options.UnicodeClasses ||
            root is not RegexSequenceNode sequence)
        {
            return false;
        }

        int index = 0;
        if (!TryConsume(sequence.Nodes, ref index, RegexSyntaxKind.StartAnchor) ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsIsoUtcTimestampCapture) ||
            !TryConsumeLiteral(sequence.Nodes, ref index, (byte)'\t') ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsAsciiWordCapture) ||
            !TryConsumeLiteral(sequence.Nodes, ref index, (byte)'\t') ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsAsciiWordCapture) ||
            !TryConsumeLiteral(sequence.Nodes, ref index, (byte)'\t') ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsNotTabCapture) ||
            !TryConsumeOptionalDetails(sequence.Nodes, ref index) ||
            !TryConsume(sequence.Nodes, ref index, RegexSyntaxKind.EndAnchor) ||
            index != sequence.Nodes.Count)
        {
            return false;
        }

        engine = new RegexTabbedLogCaptureEngine(options.LineTerminator);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!TryMatch(
            haystack,
            startAt,
            out RegexMatch timestamp,
            out RegexMatch level,
            out RegexMatch component,
            out RegexMatch message,
            out RegexMatch? details))
        {
            return null;
        }

        var match = new RegexMatch(0, haystack.Length);
        var groups = new RegexMatch?[CaptureCount + 1];
        groups[0] = match;
        groups[1] = timestamp;
        groups[2] = level;
        groups[3] = component;
        groups[4] = message;
        groups[5] = details;
        return new RegexCaptures(match, groups);
    }

    public long CountCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatch(haystack, startAt, out _, out _, out _, out _, out RegexMatch? details)
            ? details.HasValue ? 6 : 5
            : 0;
    }

    private bool TryMatch(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out RegexMatch timestamp,
        out RegexMatch level,
        out RegexMatch component,
        out RegexMatch message,
        out RegexMatch? details)
    {
        timestamp = default;
        level = default;
        component = default;
        message = default;
        details = null;

        if (Math.Clamp(startAt, 0, haystack.Length) != 0 ||
            !TryConsumeTimestamp(haystack, out int position) ||
            !TryConsumeLiteral(haystack, ref position, (byte)'\t'))
        {
            return false;
        }

        int levelStart = position;
        if (!TryConsumeAsciiWordRun(haystack, ref position) ||
            !TryConsumeLiteral(haystack, ref position, (byte)'\t'))
        {
            return false;
        }

        level = new RegexMatch(levelStart, position - levelStart - 1);

        int componentStart = position;
        if (!TryConsumeAsciiWordRun(haystack, ref position) ||
            !TryConsumeLiteral(haystack, ref position, (byte)'\t'))
        {
            return false;
        }

        component = new RegexMatch(componentStart, position - componentStart - 1);

        int messageStart = position;
        int messageEnd = haystack[position..].IndexOf((byte)'\t');
        if (messageEnd < 0)
        {
            messageEnd = haystack.Length;
        }
        else
        {
            messageEnd += position;
        }

        if (messageEnd == messageStart)
        {
            return false;
        }

        message = new RegexMatch(messageStart, messageEnd - messageStart);
        position = messageEnd;
        if (position == haystack.Length)
        {
            timestamp = new RegexMatch(0, 20);
            return true;
        }

        position++;
        int detailsStart = position;
        if (detailsStart == haystack.Length)
        {
            return false;
        }

        while (position < haystack.Length)
        {
            if (haystack[position] == lineTerminator)
            {
                return false;
            }

            position++;
        }

        timestamp = new RegexMatch(0, 20);
        details = new RegexMatch(detailsStart, haystack.Length - detailsStart);
        return true;
    }

    private static bool TryConsumeTimestamp(ReadOnlySpan<byte> haystack, out int position)
    {
        position = 0;
        if (haystack.Length < 20 ||
            !IsDigit(haystack[0]) ||
            !IsDigit(haystack[1]) ||
            !IsDigit(haystack[2]) ||
            !IsDigit(haystack[3]) ||
            haystack[4] != (byte)'-' ||
            !IsDigit(haystack[5]) ||
            !IsDigit(haystack[6]) ||
            haystack[7] != (byte)'-' ||
            !IsDigit(haystack[8]) ||
            !IsDigit(haystack[9]) ||
            haystack[10] != (byte)'T' ||
            !IsDigit(haystack[11]) ||
            !IsDigit(haystack[12]) ||
            haystack[13] != (byte)':' ||
            !IsDigit(haystack[14]) ||
            !IsDigit(haystack[15]) ||
            haystack[16] != (byte)':' ||
            !IsDigit(haystack[17]) ||
            !IsDigit(haystack[18]) ||
            haystack[19] != (byte)'Z')
        {
            return false;
        }

        position = 20;
        return true;
    }

    private static bool TryConsumeAsciiWordRun(ReadOnlySpan<byte> haystack, ref int position)
    {
        int start = position;
        while (position < haystack.Length && IsAsciiWord(haystack[position]))
        {
            position++;
        }

        return position > start;
    }

    private static bool TryConsumeLiteral(ReadOnlySpan<byte> haystack, ref int position, byte value)
    {
        if ((uint)position >= (uint)haystack.Length || haystack[position] != value)
        {
            return false;
        }

        position++;
        return true;
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
        Func<RegexSyntaxNode, int, bool> predicate)
    {
        if (index >= nodes.Count || !predicate(nodes[index], index))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeLiteral(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, byte value)
    {
        if (index >= nodes.Count || !IsLiteral(nodes[index], value))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeOptionalDetails(IReadOnlyList<RegexSyntaxNode> nodes, ref int index)
    {
        if (index >= nodes.Count ||
            UnwrapTransparentNonCapturingGroups(nodes[index]) is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: 1,
                Lazy: false,
            } repetition ||
            UnwrapTransparentNonCapturingGroups(repetition.Child) is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            !IsLiteral(sequence.Nodes[0], (byte)'\t') ||
            !IsDotPlusCapture(sequence.Nodes[1], CaptureCount))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool IsIsoUtcTimestampCapture(RegexSyntaxNode node, int sequenceIndex)
    {
        return sequenceIndex == 1 &&
            TryGetCapture(node, 1, out RegexSyntaxNode child) &&
            UnwrapTransparentNonCapturingGroups(child) is RegexSequenceNode { Nodes.Count: 12 } sequence &&
            IsDigitRepetition(sequence.Nodes[0], 4) &&
            IsLiteral(sequence.Nodes[1], (byte)'-') &&
            IsDigitRepetition(sequence.Nodes[2], 2) &&
            IsLiteral(sequence.Nodes[3], (byte)'-') &&
            IsDigitRepetition(sequence.Nodes[4], 2) &&
            IsLiteral(sequence.Nodes[5], (byte)'T') &&
            IsDigitRepetition(sequence.Nodes[6], 2) &&
            IsLiteral(sequence.Nodes[7], (byte)':') &&
            IsDigitRepetition(sequence.Nodes[8], 2) &&
            IsLiteral(sequence.Nodes[9], (byte)':') &&
            IsDigitRepetition(sequence.Nodes[10], 2) &&
            IsLiteral(sequence.Nodes[11], (byte)'Z');
    }

    private static bool IsAsciiWordCapture(RegexSyntaxNode node, int sequenceIndex)
    {
        int captureIndex = sequenceIndex == 3 ? 2 : sequenceIndex == 5 ? 3 : 0;
        return captureIndex > 0 &&
            TryGetCapture(node, captureIndex, out RegexSyntaxNode child) &&
            UnwrapTransparentNonCapturingGroups(child) is RegexRepetitionNode
            {
                Minimum: 1,
                Maximum: null,
                Lazy: false,
            } repetition &&
            IsAtom(repetition.Child, RegexSyntaxKind.WordClass);
    }

    private static bool IsNotTabCapture(RegexSyntaxNode node, int sequenceIndex)
    {
        return sequenceIndex == 7 &&
            TryGetCapture(node, 4, out RegexSyntaxNode child) &&
            UnwrapTransparentNonCapturingGroups(child) is RegexRepetitionNode
            {
                Minimum: 1,
                Maximum: null,
                Lazy: false,
            } repetition &&
            IsNegatedSingleByteClass(repetition.Child, (byte)'\t');
    }

    private static bool IsDotPlusCapture(RegexSyntaxNode node, int captureIndex)
    {
        return TryGetCapture(node, captureIndex, out RegexSyntaxNode child) &&
            UnwrapTransparentNonCapturingGroups(child) is RegexRepetitionNode
            {
                Minimum: 1,
                Maximum: null,
                Lazy: false,
            } repetition &&
            IsAtom(repetition.Child, RegexSyntaxKind.Dot);
    }

    private static bool IsDigitRepetition(RegexSyntaxNode node, int count)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
            {
                Minimum: var minimum,
                Maximum: var maximum,
                Lazy: false,
            } repetition &&
            minimum == count &&
            maximum == count &&
            IsDigitAtom(repetition.Child);
    }

    private static bool TryGetCapture(RegexSyntaxNode node, int captureIndex, out RegexSyntaxNode child)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: var actualCaptureIndex,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group &&
            actualCaptureIndex == captureIndex)
        {
            child = group.Child;
            return true;
        }

        child = node;
        return false;
    }

    private static bool IsAtom(RegexSyntaxNode node, RegexSyntaxKind kind)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode atom && atom.Kind == kind;
    }

    private static bool IsDigitAtom(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.DigitClass } ||
            node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            atom.Value.Span.SequenceEqual("0-9"u8);
    }

    private static bool IsLiteral(RegexSyntaxNode node, byte value)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Length == 1 &&
            atom.Value.Span[0] == value;
    }

    private static bool IsNegatedSingleByteClass(RegexSyntaxNode node, byte value)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            atom.Value.Length >= 2 &&
            atom.Value.Span[0] == (byte)'^' &&
            TryDecodeSingleClassByte(atom.Value.Span[1..], out byte excluded) &&
            excluded == value;
    }

    private static bool TryDecodeSingleClassByte(ReadOnlySpan<byte> expression, out byte value)
    {
        value = default;
        if (expression.IsEmpty)
        {
            return false;
        }

        if (expression[0] == (byte)'\\')
        {
            if (expression.Length != 2)
            {
                return false;
            }

            value = expression[1] switch
            {
                (byte)'t' => (byte)'\t',
                (byte)'n' => (byte)'\n',
                (byte)'r' => (byte)'\r',
                (byte)'f' => (byte)'\f',
                _ => expression[1],
            };
            return true;
        }

        if (expression.Length != 1)
        {
            return false;
        }

        value = expression[0];
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

    private static bool IsDigit(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }

    private static bool IsAsciiWord(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z' ||
            value is >= (byte)'0' and <= (byte)'9' ||
            value == (byte)'_';
    }
}
