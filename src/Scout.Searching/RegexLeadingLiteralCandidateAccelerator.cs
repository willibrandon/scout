namespace Scout;

internal sealed class RegexLeadingLiteralCandidateAccelerator
{
    private readonly byte literal;
    private readonly int minimumCallCommas;

    private RegexLeadingLiteralCandidateAccelerator(byte literal, int minimumCallCommas)
    {
        this.literal = literal;
        this.minimumCallCommas = minimumCallCommas;
    }

    public static bool TryCompile(
        ReadOnlySpan<byte> pattern,
        bool asciiCaseInsensitive,
        out RegexLeadingLiteralCandidateAccelerator? accelerator)
    {
        accelerator = null;
        RegexSyntaxTree tree;
        try
        {
            tree = RegexSyntaxParser.Parse(pattern);
        }
        catch (FormatException)
        {
            return false;
        }

        if (tree.Root is not RegexSequenceNode sequence)
        {
            return false;
        }

        IReadOnlyList<RegexSyntaxNode> nodes = sequence.Nodes;
        if (nodes.Count < 2)
        {
            return false;
        }

        int index = 0;
        if (!IsUnboundedRepeat(nodes[index], RegexSyntaxKind.WordClass, minimum: 1))
        {
            return false;
        }

        index++;
        if (index < nodes.Count && IsUnboundedRepeat(nodes[index], RegexSyntaxKind.WhitespaceClass, minimum: 0))
        {
            index++;
        }

        if (index >= nodes.Count ||
            nodes[index] is not RegexAtomNode literalNode ||
            literalNode.Kind != RegexSyntaxKind.Literal ||
            literalNode.Value.Length != 1)
        {
            return false;
        }

        byte literal = literalNode.Value.Span[0];
        if (asciiCaseInsensitive && IsAsciiLetter(literal))
        {
            return false;
        }

        int minimumCallCommas = TryGetMinimumCallCommas(nodes, index + 1, out int callCommas)
            ? callCommas
            : 0;

        accelerator = new RegexLeadingLiteralCandidateAccelerator(literal, minimumCallCommas);
        return true;
    }

    public bool TryFind(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        int offset,
        bool asciiCaseInsensitive,
        out int matchStart,
        out int matchLength)
    {
        int search = Math.Clamp(offset, 0, haystack.Length);
        while (search < haystack.Length)
        {
            int found = haystack[search..].IndexOf(literal);
            if (found < 0)
            {
                break;
            }

            int anchor = search + found;
            int candidateStart = FindCandidateStart(haystack, offset, anchor);
            if (candidateStart >= 0 && minimumCallCommas > 0)
            {
                if (TryMatchCallWithMinimumCommas(haystack, candidateStart, anchor, minimumCallCommas, out matchLength))
                {
                    matchStart = candidateStart;
                    return true;
                }
            }
            else if (candidateStart >= 0 &&
                LiteralLineSearcher.TryMatchRegexAt(
                    haystack,
                    pattern,
                    candidateStart,
                    asciiCaseInsensitive,
                    ignoreWhitespace: false,
                    swapGreed: false,
                    out matchLength))
            {
                matchStart = candidateStart;
                return true;
            }

            search = anchor + 1;
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private static bool TryMatchCallWithMinimumCommas(
        ReadOnlySpan<byte> haystack,
        int candidateStart,
        int openParen,
        int minimumCommas,
        out int matchLength)
    {
        int commas = 0;
        int index = openParen + 1;
        while (index < haystack.Length)
        {
            byte value = haystack[index];
            if (value == (byte)')')
            {
                if (commas >= minimumCommas)
                {
                    matchLength = index + 1 - candidateStart;
                    return true;
                }

                break;
            }

            if (value == (byte)',')
            {
                commas++;
            }

            index++;
        }

        matchLength = 0;
        return false;
    }

    private static bool TryGetMinimumCallCommas(IReadOnlyList<RegexSyntaxNode> nodes, int index, out int minimumCommas)
    {
        minimumCommas = 0;
        if (index + 3 != nodes.Count)
        {
            return false;
        }

        if (!IsNotRightParenStar(nodes[index]))
        {
            return false;
        }

        if (nodes[index + 1] is not RegexRepetitionNode commaGroup ||
            commaGroup.Minimum <= 0 ||
            commaGroup.Maximum is not null ||
            commaGroup.Lazy ||
            commaGroup.Child is not RegexGroupNode group ||
            !IsCommaNotRightParenStarSequence(group.Child))
        {
            return false;
        }

        if (nodes[index + 2] is not RegexAtomNode closeParen ||
            closeParen.Kind != RegexSyntaxKind.Literal ||
            closeParen.Value.Length != 1 ||
            closeParen.Value.Span[0] != (byte)')')
        {
            return false;
        }

        minimumCommas = commaGroup.Minimum;
        return true;
    }

    private static bool IsCommaNotRightParenStarSequence(RegexSyntaxNode node)
    {
        if (node is not RegexSequenceNode sequence || sequence.Nodes.Count != 2)
        {
            return false;
        }

        return sequence.Nodes[0] is RegexAtomNode comma &&
            comma.Kind == RegexSyntaxKind.Literal &&
            comma.Value.Length == 1 &&
            comma.Value.Span[0] == (byte)',' &&
            IsNotRightParenStar(sequence.Nodes[1]);
    }

    private static bool IsNotRightParenStar(RegexSyntaxNode node)
    {
        return node is RegexRepetitionNode repetition &&
            repetition.Minimum == 0 &&
            repetition.Maximum is null &&
            !repetition.Lazy &&
            repetition.Child is RegexAtomNode atom &&
            atom.Kind == RegexSyntaxKind.CharacterClass &&
            IsNegatedSingleLiteral(atom.Value.Span, (byte)')');
    }

    private static bool IsNegatedSingleLiteral(ReadOnlySpan<byte> expression, byte literal)
    {
        return expression.Length == 2 &&
            expression[0] == (byte)'^' &&
            expression[1] == literal;
    }

    private static int FindCandidateStart(ReadOnlySpan<byte> haystack, int offset, int anchor)
    {
        int index = anchor - 1;
        while (index >= offset && IsRegexWhitespaceByte(haystack[index]))
        {
            index--;
        }

        int wordEnd = index + 1;
        while (index >= offset && IsAsciiWordByte(haystack[index]))
        {
            index--;
        }

        int wordStart = index + 1;
        if (wordStart >= wordEnd || offset > wordEnd)
        {
            return -1;
        }

        return Math.Max(wordStart, offset);
    }

    private static bool IsUnboundedRepeat(RegexSyntaxNode node, RegexSyntaxKind childKind, int minimum)
    {
        return node is RegexRepetitionNode repetition &&
            repetition.Minimum == minimum &&
            repetition.Maximum is null &&
            !repetition.Lazy &&
            repetition.Child is RegexAtomNode atom &&
            atom.Kind == childKind;
    }

    private static bool IsAsciiWordByte(byte value)
    {
        return value == (byte)'_'
            || (value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z');
    }

    private static bool IsRegexWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\f' or 0x0b;
    }

    private static bool IsAsciiLetter(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }
}
