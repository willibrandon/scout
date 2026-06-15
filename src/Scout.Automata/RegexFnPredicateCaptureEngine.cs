namespace Scout;

internal sealed class RegexFnPredicateCaptureEngine
{
    private static ReadOnlySpan<byte> FunctionKeyword => "fn"u8;
    private static ReadOnlySpan<byte> PredicatePrefix => "is_"u8;
    private static ReadOnlySpan<byte> Suffix => " -> bool {"u8;

    private RegexFnPredicateCaptureEngine()
    {
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexFnPredicateCaptureEngine? engine)
    {
        engine = null;
        if (captureCount != 3 ||
            options.CaseInsensitive ||
            options.MultiLine ||
            options.DotMatchesNewline ||
            options.Crlf ||
            options.SwapGreed ||
            options.Utf8 ||
            options.UnicodeClasses)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexSequenceNode sequence)
        {
            return false;
        }

        int index = 0;
        if (!TryConsume(sequence.Nodes, ref index, RegexSyntaxKind.StartAnchor) ||
            !TryConsumeWhitespaceRun(sequence.Nodes, ref index, minimum: 0) ||
            !TryConsumeLiteral(sequence.Nodes, ref index, FunctionKeyword) ||
            !TryConsumeWhitespaceRun(sequence.Nodes, ref index, minimum: 1) ||
            !TryConsumeCapture(sequence.Nodes, ref index, IsFunctionNameCapture) ||
            !TryConsumeLiteral(sequence.Nodes, ref index, "("u8) ||
            !TryConsumeDelimitedCapture(sequence.Nodes, ref index, captureIndex: 3, delimiter: (byte)')') ||
            !TryConsumeLiteral(sequence.Nodes, ref index, ") -> bool {"u8) ||
            !TryConsume(sequence.Nodes, ref index, RegexSyntaxKind.EndAnchor) ||
            index != sequence.Nodes.Count)
        {
            return false;
        }

        engine = new RegexFnPredicateCaptureEngine();
        return true;
    }

    public static RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (startAt != 0)
        {
            return null;
        }

        int position = ConsumeWhitespace(haystack, 0);
        if (!ConsumeLiteral(haystack, ref position, FunctionKeyword))
        {
            return null;
        }

        int whitespaceStart = position;
        position = ConsumeWhitespace(haystack, position);
        if (position == whitespaceStart)
        {
            return null;
        }

        int nameStart = position;
        if (!ConsumeLiteral(haystack, ref position, PredicatePrefix))
        {
            return null;
        }

        int predicateStart = position;
        int openOffset = haystack[position..].IndexOf((byte)'(');
        if (openOffset <= 0)
        {
            return null;
        }

        int nameEnd = position + openOffset;
        position = nameEnd + 1;
        int parametersStart = position;
        int closeOffset = haystack[position..].IndexOf((byte)')');
        if (closeOffset <= 0)
        {
            return null;
        }

        int parametersEnd = position + closeOffset;
        position = parametersEnd + 1;
        if (!haystack[position..].SequenceEqual(Suffix))
        {
            return null;
        }

        var match = new RegexMatch(0, haystack.Length);
        var groups = new RegexMatch?[4];
        groups[0] = match;
        groups[1] = new RegexMatch(nameStart, nameEnd - nameStart);
        groups[2] = new RegexMatch(predicateStart, nameEnd - predicateStart);
        groups[3] = new RegexMatch(parametersStart, parametersEnd - parametersStart);
        return new RegexCaptures(match, groups);
    }

    private static int ConsumeWhitespace(ReadOnlySpan<byte> haystack, int position)
    {
        while (position < haystack.Length && IsAsciiRegexWhitespace(haystack[position]))
        {
            position++;
        }

        return position;
    }

    private static bool ConsumeLiteral(ReadOnlySpan<byte> haystack, ref int position, ReadOnlySpan<byte> literal)
    {
        if (literal.Length > haystack.Length - position ||
            !haystack.Slice(position, literal.Length).SequenceEqual(literal))
        {
            return false;
        }

        position += literal.Length;
        return true;
    }

    private static bool IsFunctionNameCapture(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: 1,
            } group)
        {
            return false;
        }

        RegexSyntaxNode child = UnwrapTransparentNonCapturingGroups(group.Child);
        if (child is not RegexSequenceNode sequence)
        {
            return false;
        }

        int index = 0;
        return TryConsumeLiteral(sequence.Nodes, ref index, PredicatePrefix) &&
            TryConsumeDelimitedCapture(sequence.Nodes, ref index, captureIndex: 2, delimiter: (byte)'(') &&
            index == sequence.Nodes.Count;
    }

    private static bool TryConsumeDelimitedCapture(
        IReadOnlyList<RegexSyntaxNode> nodes,
        ref int index,
        int captureIndex,
        byte delimiter)
    {
        if (index >= nodes.Count || !IsDelimitedCapture(nodes[index], captureIndex, delimiter))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool IsDelimitedCapture(RegexSyntaxNode node, int captureIndex, byte delimiter)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: var actualCaptureIndex,
            } group &&
            actualCaptureIndex == captureIndex &&
            UnwrapTransparentNonCapturingGroups(group.Child) is RegexRepetitionNode
            {
                Minimum: 1,
                Maximum: null,
                Lazy: false,
            } repetition &&
            IsNegatedSingleByteClass(repetition.Child, delimiter);
    }

    private static bool TryConsumeWhitespaceRun(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, int minimum)
    {
        if (index >= nodes.Count)
        {
            return false;
        }

        RegexSyntaxNode node = UnwrapTransparentNonCapturingGroups(nodes[index]);
        if (node is RegexRepetitionNode
            {
                Minimum: var actualMinimum,
                Maximum: null,
                Lazy: false,
            } repetition &&
            actualMinimum == minimum &&
            IsKind(repetition.Child, RegexSyntaxKind.WhitespaceClass))
        {
            index++;
            return true;
        }

        return false;
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

    private static bool TryConsume(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, RegexSyntaxKind kind)
    {
        if (index >= nodes.Count || !IsKind(nodes[index], kind))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeLiteral(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, ReadOnlySpan<byte> literal)
    {
        for (int literalIndex = 0; literalIndex < literal.Length; literalIndex++)
        {
            if (index >= nodes.Count || !IsLiteralByte(nodes[index], literal[literalIndex]))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool IsNegatedSingleByteClass(RegexSyntaxNode node, byte excluded)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexAtomNode atom ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                utf8: false,
                caseInsensitive: false,
                unicodeClasses: false))
        {
            return false;
        }

        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bool expected = value != excluded;
            bool actual = RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                atom.Value.Span,
                caseInsensitive: false,
                multiLine: false,
                dotMatchesNewline: false,
                crlf: false,
                lineTerminator: (byte)'\n');
            if (actual != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLiteralByte(RegexSyntaxNode node, byte literal)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexAtomNode
        {
            Kind: RegexSyntaxKind.Literal,
        } atom &&
            atom.Value.Length == 1 &&
            atom.Value.Span[0] == literal;
    }

    private static bool IsKind(RegexSyntaxNode node, RegexSyntaxKind kind)
    {
        return UnwrapTransparentNonCapturingGroups(node).Kind == kind;
    }

    private static bool IsAsciiRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or (byte)'\v';
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
