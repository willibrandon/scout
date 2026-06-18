using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexIpv4AddressEngine
{
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
        int minimumStart = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = Math.Min(haystack.Length, minimumStart + 2);
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOf((byte)'.');
            if (offset < 0)
            {
                return null;
            }

            int dot = searchAt + offset;
            if (TryMatchBeforeFirstDot(haystack, minimumStart, dot, out int matchStart, out int matchLength))
            {
                return new RegexMatch(matchStart, matchLength);
            }

            searchAt = dot + 1;
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
        int minimumStart = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = Math.Min(haystack.Length, minimumStart + 2);
        if (Avx2.IsSupported && haystack.Length - searchAt > Vector256<byte>.Count)
        {
            return CountOrSumVector256(haystack, minimumStart, searchAt, sumSpans);
        }

        return CountOrSumScalar(haystack, minimumStart, searchAt, sumSpans, total: 0);
    }

    private static long CountOrSumVector256(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        int searchAt,
        bool sumSpans)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var dotVector = Vector256.Create((byte)'.');
        long total = 0;
        int vectorEnd = haystack.Length - Vector256<byte>.Count;
        while (searchAt <= vectorEnd)
        {
            var values = Vector256.LoadUnsafe(ref reference, (nuint)searchAt);
            uint mask = Avx2.CompareEqual(values, dotVector).ExtractMostSignificantBits();
            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                int dot = searchAt + bit;
                if (TryMatchBeforeFirstDot(haystack, minimumStart, dot, out int matchStart, out int matchLength))
                {
                    total += sumSpans ? matchLength : 1;
                    int matchEnd = matchStart + matchLength;
                    minimumStart = matchEnd;
                    searchAt = Math.Min(haystack.Length, matchEnd + 2);
                    goto NextVector;
                }

                mask &= mask - 1;
            }

            searchAt += Vector256<byte>.Count;
        NextVector:
            ;
        }

        return CountOrSumScalar(haystack, minimumStart, searchAt, sumSpans, total);
    }

    private static long CountOrSumScalar(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        int searchAt,
        bool sumSpans,
        long total)
    {
        searchAt = Math.Min(haystack.Length, Math.Max(searchAt, minimumStart + 2));
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOf((byte)'.');
            if (offset < 0)
            {
                return total;
            }

            int dot = searchAt + offset;
            if (TryMatchBeforeFirstDot(haystack, minimumStart, dot, out int matchStart, out int matchLength))
            {
                total += sumSpans ? matchLength : 1;
                int matchEnd = matchStart + matchLength;
                minimumStart = matchEnd;
                searchAt = Math.Min(haystack.Length, matchEnd + 2);
            }
            else
            {
                searchAt = dot + 1;
            }
        }

        return total;
    }

    private static bool TryMatchBeforeFirstDot(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        int dot,
        out int start,
        out int length)
    {
        int threeDigitStart = dot - 3;
        if (threeDigitStart >= minimumStart &&
            IsDigit(haystack[threeDigitStart]) &&
            IsDigit(haystack[threeDigitStart + 1]) &&
            IsDigit(haystack[threeDigitStart + 2]) &&
            TryMatchAt(haystack, threeDigitStart, out int threeDigitLength))
        {
            start = threeDigitStart;
            length = threeDigitLength;
            return true;
        }

        int twoDigitStart = dot - 2;
        if (twoDigitStart >= minimumStart &&
            IsDigit(haystack[twoDigitStart]) &&
            IsDigit(haystack[twoDigitStart + 1]) &&
            TryMatchAt(haystack, twoDigitStart, out int twoDigitLength))
        {
            start = twoDigitStart;
            length = twoDigitLength;
            return true;
        }

        start = 0;
        length = 0;
        return false;
    }

    private static bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        if (!TryReadOctet(haystack, start, out int firstLength))
        {
            length = 0;
            return false;
        }

        int position = start + firstLength;
        if (position >= haystack.Length || haystack[position] != (byte)'.')
        {
            length = 0;
            return false;
        }

        position++;
        if (!TryReadOctet(haystack, position, out int secondLength))
        {
            length = 0;
            return false;
        }

        position += secondLength;
        if (position >= haystack.Length || haystack[position] != (byte)'.')
        {
            length = 0;
            return false;
        }

        position++;
        if (!TryReadOctet(haystack, position, out int thirdLength))
        {
            length = 0;
            return false;
        }

        position += thirdLength;
        if (position >= haystack.Length || haystack[position] != (byte)'.')
        {
            length = 0;
            return false;
        }

        position++;
        if (!TryReadOctet(haystack, position, out int fourthLength))
        {
            length = 0;
            return false;
        }

        length = position + fourthLength - start;
        return true;
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
