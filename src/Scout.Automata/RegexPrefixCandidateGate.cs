namespace Scout;

internal sealed class RegexPrefixCandidateGate
{
    private readonly byte[][] prefixes;
    private readonly bool[] terminators;
    private readonly byte requiredLiteral;
    private readonly bool canSkipPastTerminator;

    private RegexPrefixCandidateGate(byte[][] prefixes, bool[] terminators, byte requiredLiteral)
    {
        this.prefixes = prefixes;
        this.terminators = terminators;
        this.requiredLiteral = requiredLiteral;
        canSkipPastTerminator = CanSkipPastTerminator(prefixes, terminators);
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        byte[][]? prefixes,
        out RegexPrefixCandidateGate? gate)
    {
        gate = null;
        if (prefixes is null ||
            prefixes.Length == 0 ||
            options.CaseInsensitive)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode sequence)
        {
            return false;
        }

        int index = 0;
        if (!TryReadWholeLiteralAlternatives(sequence.Nodes, ref index, prefixes) ||
            !TryReadNegatedLiteralByteSetStar(sequence.Nodes, ref index, out bool[] terminators) ||
            !TryReadOneByteLiteral(sequence.Nodes, ref index, out byte requiredLiteral))
        {
            return false;
        }

        gate = new RegexPrefixCandidateGate(prefixes, terminators, requiredLiteral);
        return true;
    }

    public bool CanMatch(ReadOnlySpan<byte> haystack, int candidate, out int resumeAt)
    {
        resumeAt = candidate + 1;
        if (!TryGetPrefixLength(haystack, candidate, out int prefixLength))
        {
            return true;
        }

        for (int index = candidate + prefixLength; index < haystack.Length; index++)
        {
            byte value = haystack[index];
            if (value == requiredLiteral)
            {
                return true;
            }

            if (terminators[value])
            {
                resumeAt = canSkipPastTerminator ? index + 1 : candidate + 1;
                return false;
            }
        }

        resumeAt = haystack.Length;
        return false;
    }

    private bool TryGetPrefixLength(ReadOnlySpan<byte> haystack, int candidate, out int prefixLength)
    {
        for (int index = 0; index < prefixes.Length; index++)
        {
            ReadOnlySpan<byte> prefix = prefixes[index];
            if (candidate <= haystack.Length - prefix.Length &&
                haystack.Slice(candidate, prefix.Length).SequenceEqual(prefix))
            {
                prefixLength = prefix.Length;
                return true;
            }
        }

        prefixLength = 0;
        return false;
    }

    private static bool CanSkipPastTerminator(byte[][] prefixes, bool[] terminators)
    {
        for (int index = 0; index < prefixes.Length; index++)
        {
            if (terminators[prefixes[index][0]])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadWholeLiteralAlternatives(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, byte[][] expectedPrefixes)
    {
        if (index >= nodes.Count ||
            !TryReadWholeLiteralAlternatives(nodes[index], out byte[][] prefixes) ||
            prefixes.Length != expectedPrefixes.Length)
        {
            return false;
        }

        for (int prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
        {
            if (!prefixes[prefixIndex].AsSpan().SequenceEqual(expectedPrefixes[prefixIndex]))
            {
                return false;
            }
        }

        index++;
        return true;
    }

    private static bool TryReadWholeLiteralAlternatives(RegexSyntaxNode node, out byte[][] prefixes)
    {
        node = UnwrapTransparentGroups(node);
        if (node is RegexAlternationNode alternation)
        {
            byte[][] alternatives = new byte[alternation.Alternatives.Count][];
            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryReadWholeLiteralSequence(alternation.Alternatives[index], out byte[] literal) ||
                    literal.Length == 0)
                {
                    prefixes = [];
                    return false;
                }

                alternatives[index] = literal;
            }

            prefixes = alternatives;
            return prefixes.Length > 0;
        }

        if (TryReadWholeLiteralSequence(node, out byte[] prefix) &&
            prefix.Length > 0)
        {
            prefixes = [prefix];
            return true;
        }

        prefixes = [];
        return false;
    }

    private static bool TryReadWholeLiteralSequence(RegexSyntaxNode node, out byte[] literal)
    {
        node = UnwrapTransparentGroups(node);
        if (node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
        {
            literal = atom.Value.ToArray();
            return true;
        }

        if (node is not RegexSequenceNode sequence)
        {
            literal = [];
            return false;
        }

        var bytes = new List<byte>();
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = UnwrapTransparentGroups(sequence.Nodes[index]);
            if (child is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } childAtom)
            {
                literal = [];
                return false;
            }

            bytes.AddRange(childAtom.Value.ToArray());
        }

        literal = bytes.ToArray();
        return true;
    }

    private static bool TryReadNegatedLiteralByteSetStar(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, out bool[] terminators)
    {
        terminators = [];
        if (index >= nodes.Count ||
            UnwrapTransparentGroups(nodes[index]) is not RegexRepetitionNode { Minimum: 0, Maximum: null } repetition ||
            UnwrapTransparentGroups(repetition.Child) is not RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom ||
            !TryReadNegatedLiteralByteSet(atom.Value.Span, out terminators))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryReadNegatedLiteralByteSet(ReadOnlySpan<byte> expression, out bool[] terminators)
    {
        terminators = new bool[256];
        if (expression.Length < 2 ||
            expression[0] != (byte)'^')
        {
            return false;
        }

        int index = 1;
        while (index < expression.Length)
        {
            if (!TryReadLiteralClassByte(expression, ref index, out byte literal))
            {
                return false;
            }

            if (index < expression.Length &&
                expression[index] == (byte)'-')
            {
                return false;
            }

            terminators[literal] = true;
        }

        return true;
    }

    private static bool TryReadLiteralClassByte(ReadOnlySpan<byte> expression, ref int index, out byte literal)
    {
        literal = 0;
        if (index >= expression.Length)
        {
            return false;
        }

        byte value = expression[index++];
        if (value != (byte)'\\')
        {
            literal = value;
            return true;
        }

        if (index >= expression.Length)
        {
            return false;
        }

        byte escaped = expression[index++];
        literal = escaped switch
        {
            (byte)'t' => (byte)'\t',
            (byte)'r' => (byte)'\r',
            (byte)'f' => (byte)'\f',
            _ => escaped,
        };
        return escaped is not ((byte)'d' or (byte)'D' or (byte)'w' or (byte)'W' or (byte)'s' or (byte)'S' or (byte)'p' or (byte)'P');
    }

    private static bool TryReadOneByteLiteral(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, out byte literal)
    {
        literal = 0;
        if (index >= nodes.Count ||
            UnwrapTransparentGroups(nodes[index]) is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom ||
            atom.Value.Length != 1)
        {
            return false;
        }

        literal = atom.Value.Span[0];
        index++;
        return true;
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
