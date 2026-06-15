using System.Buffers;

namespace Scout;

internal sealed class RegexIpv4AddressEngine
{
    private static readonly SearchValues<byte> Digits = SearchValues.Create("0123456789"u8);

    private RegexIpv4AddressEngine()
    {
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexIpv4AddressEngine? engine)
    {
        engine = null;
        if (options.Utf8 || !IsIpv4AddressPattern(root))
        {
            return false;
        }

        engine = new RegexIpv4AddressEngine();
        return true;
    }

    public static RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(Digits);
            if (offset < 0)
            {
                return null;
            }

            int start = searchAt + offset;
            if (TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }

            searchAt = start + 1;
        }

        return null;
    }

    public static RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, start, out int length)
            ? new RegexMatch(start, length)
            : null;
    }

    public static long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    public static long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    private static long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(Digits);
            if (offset < 0)
            {
                return total;
            }

            int start = searchAt + offset;
            if (TryMatchAt(haystack, start, out int length))
            {
                total += sumSpans ? length : 1;
                searchAt = start + length;
            }
            else
            {
                searchAt = start + 1;
            }
        }

        return total;
    }

    private static bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int position = start;
        for (int octet = 0; octet < 4; octet++)
        {
            if (!TryReadOctet(haystack, position, out int octetLength))
            {
                length = 0;
                return false;
            }

            position += octetLength;
            if (octet == 3)
            {
                length = position - start;
                return true;
            }

            if (position >= haystack.Length || haystack[position] != (byte)'.')
            {
                length = 0;
                return false;
            }

            position++;
        }

        length = 0;
        return false;
    }

    private static bool TryReadOctet(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        if (position + 1 >= haystack.Length ||
            !IsDigit(haystack[position]) ||
            !IsDigit(haystack[position + 1]))
        {
            return false;
        }

        byte first = haystack[position];
        byte second = haystack[position + 1];
        if (position + 2 < haystack.Length && IsDigit(haystack[position + 2]))
        {
            byte third = haystack[position + 2];
            if (first is (byte)'0' or (byte)'1' ||
                first == (byte)'2' &&
                (second is >= (byte)'0' and <= (byte)'4' ||
                    second == (byte)'5' && third is >= (byte)'0' and <= (byte)'5'))
            {
                length = 3;
                return true;
            }

            return false;
        }

        length = 2;
        return true;
    }

    private static bool IsDigit(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }

    private static bool IsIpv4AddressPattern(RegexSyntaxNode root)
    {
        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 2 } sequence)
        {
            return false;
        }

        return IsRepeatedOctetDot(sequence.Nodes[0]) && IsOctet(sequence.Nodes[1]);
    }

    private static bool IsRepeatedOctetDot(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode { Minimum: 3, Maximum: 3, Lazy: false } repetition)
        {
            return false;
        }

        RegexSyntaxNode child = UnwrapTransparentGroups(repetition.Child);
        return child is RegexSequenceNode { Nodes.Count: 2 } sequence &&
            IsOctet(sequence.Nodes[0]) &&
            IsLiteral(sequence.Nodes[1], "."u8);
    }

    private static bool IsOctet(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAlternationNode { Alternatives.Count: 3 } alternation)
        {
            return false;
        }

        return IsFirstOctetAlternative(alternation.Alternatives[0]) &&
            IsSecondOctetAlternative(alternation.Alternatives[1]) &&
            IsThirdOctetAlternative(alternation.Alternatives[2]);
    }

    private static bool IsFirstOctetAlternative(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsLiteral(sequence.Nodes[0], "2"u8) &&
            IsLiteral(sequence.Nodes[1], "5"u8) &&
            IsClass(sequence.Nodes[2], "0-5"u8);
    }

    private static bool IsSecondOctetAlternative(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsLiteral(sequence.Nodes[0], "2"u8) &&
            IsClass(sequence.Nodes[1], "0-4"u8) &&
            IsClass(sequence.Nodes[2], "0-9"u8);
    }

    private static bool IsThirdOctetAlternative(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsOptionalZeroOneClass(sequence.Nodes[0]) &&
            IsClass(sequence.Nodes[1], "0-9"u8) &&
            IsClass(sequence.Nodes[2], "0-9"u8);
    }

    private static bool IsOptionalZeroOneClass(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode { Minimum: 0, Maximum: 1, Lazy: false } repetition &&
            IsClass(repetition.Child, "01"u8);
    }

    private static bool IsLiteral(RegexSyntaxNode node, ReadOnlySpan<byte> literal)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Span.SequenceEqual(literal);
    }

    private static bool IsClass(RegexSyntaxNode node, ReadOnlySpan<byte> expression)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            atom.Value.Span.SequenceEqual(expression);
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
