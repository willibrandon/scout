using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexRepeatedLazyDotStarLiteralEngine
{
    private const int MaxRepeat = 256;

    private readonly byte delimiter;
    private readonly byte[] suffix;
    private readonly byte suffixAnchor;
    private readonly int repeatCount;
    private readonly byte lineTerminator;

    private RegexRepeatedLazyDotStarLiteralEngine(
        byte delimiter,
        byte[] suffix,
        int repeatCount,
        byte lineTerminator)
    {
        this.delimiter = delimiter;
        this.suffix = suffix;
        suffixAnchor = suffix[0];
        this.repeatCount = repeatCount;
        this.lineTerminator = lineTerminator;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexRepeatedLazyDotStarLiteralEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.DotMatchesNewline ||
            options.Crlf ||
            options.Utf8 ||
            options.UnicodeClasses ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            !TryGetRepeatedLazyDotStarDelimiter(
                sequence.Nodes[0],
                out byte delimiter,
                out int repeatCount) ||
            !TryGetAsciiLiteral(sequence.Nodes[1], out byte[] suffix))
        {
            return false;
        }

        engine = new RegexRepeatedLazyDotStarLiteralEngine(
            delimiter,
            suffix,
            repeatCount,
            options.LineTerminator);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = lowerBound;
        while (TryFindDelimitedSuffix(haystack, searchAt, haystack.Length, lowerBound, out int delimiterStart))
        {
            int start = Math.Max(lowerBound, FindLineStart(haystack, delimiterStart));
            long delimiterCount = ByteCounter.Count(haystack[start..(delimiterStart + 1)], delimiter);
            if (delimiterCount >= repeatCount)
            {
                return new RegexMatch(start, delimiterStart + 1 + suffix.Length - start);
            }

            searchAt = delimiterStart + 1;
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int lineEnd = FindLineEnd(haystack, start);
        return TryMatchInLine(haystack, start, lineEnd, out int length)
            ? new RegexMatch(start, length)
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (Find(haystack, offset) is RegexMatch match)
        {
            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return total;
    }

    private bool TryMatchInLine(ReadOnlySpan<byte> haystack, int start, int lineEnd, out int length)
    {
        length = 0;
        if (suffix.Length > lineEnd - start)
        {
            return false;
        }

        long delimiterCount = 0;
        int countedUntil = start;
        int searchAt = start;
        while (TryFindDelimitedSuffix(haystack, searchAt, lineEnd, start, out int delimiterStart))
        {
            int delimiterEnd = delimiterStart + 1;
            delimiterCount += ByteCounter.Count(haystack[countedUntil..delimiterEnd], delimiter);
            countedUntil = delimiterEnd;
            if (delimiterCount >= repeatCount)
            {
                length = delimiterStart + 1 + suffix.Length - start;
                return true;
            }

            searchAt = delimiterStart + 1;
        }

        return false;
    }

    private bool TryFindDelimitedSuffix(
        ReadOnlySpan<byte> haystack,
        int searchAt,
        int lineEnd,
        int matchStart,
        out int delimiterStart)
    {
        delimiterStart = 0;
        if (suffix.Length == 1)
        {
            int found = FindAdjacentBytes(haystack, searchAt, lineEnd, delimiter, suffixAnchor);
            if (found < 0)
            {
                return false;
            }

            delimiterStart = found;
            return true;
        }

        while (TryFindSuffix(haystack, searchAt, lineEnd, out int suffixStart))
        {
            if (suffixStart > matchStart && haystack[suffixStart - 1] == delimiter)
            {
                delimiterStart = suffixStart - 1;
                return true;
            }

            searchAt = suffixStart + 1;
        }

        return false;
    }

    private bool TryFindSuffix(ReadOnlySpan<byte> haystack, int searchAt, int lineEnd, out int suffixStart)
    {
        suffixStart = 0;
        int lastStart = lineEnd - suffix.Length;
        while (searchAt <= lastStart)
        {
            int offset = haystack[searchAt..lineEnd].IndexOf(suffixAnchor);
            if (offset < 0)
            {
                return false;
            }

            int candidate = searchAt + offset;
            if (candidate <= lastStart &&
                (suffix.Length == 1 || haystack.Slice(candidate, suffix.Length).SequenceEqual(suffix)))
            {
                suffixStart = candidate;
                return true;
            }

            searchAt = candidate + 1;
        }

        return false;
    }

    private static int FindAdjacentBytes(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        byte first,
        byte second)
    {
        int lastStart = end - 2;
        if (start > lastStart)
        {
            return -1;
        }

        if (Avx2.IsSupported && end - start > Vector256<byte>.Count)
        {
            return FindAdjacentBytesVector256(haystack, start, end, first, second);
        }

        if (Sse2.IsSupported && end - start > Vector128<byte>.Count)
        {
            return FindAdjacentBytesVector128(haystack, start, end, first, second);
        }

        return FindAdjacentBytesScalar(haystack, start, lastStart, first, second);
    }

    private static int FindAdjacentBytesVector256(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        byte first,
        byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector256.Create(first);
        var secondVector = Vector256.Create(second);
        int width = Vector256<byte>.Count;
        int vectorLimit = end - width - 1;
        int offset = start;
        while (offset <= vectorLimit)
        {
            var current = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1));
            Vector256<byte> matches = Avx2.And(
                Avx2.CompareEqual(current, firstVector),
                Avx2.CompareEqual(next, secondVector));
            uint mask = matches.ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += width;
        }

        return FindAdjacentBytesScalar(haystack, offset, end - 2, first, second);
    }

    private static int FindAdjacentBytesVector128(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        byte first,
        byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        int width = Vector128<byte>.Count;
        int vectorLimit = end - width - 1;
        int offset = start;
        while (offset <= vectorLimit)
        {
            var current = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1));
            Vector128<byte> matches = Sse2.And(
                Sse2.CompareEqual(current, firstVector),
                Sse2.CompareEqual(next, secondVector));
            uint mask = matches.ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += width;
        }

        return FindAdjacentBytesScalar(haystack, offset, end - 2, first, second);
    }

    private static int FindAdjacentBytesScalar(
        ReadOnlySpan<byte> haystack,
        int start,
        int lastStart,
        byte first,
        byte second)
    {
        for (int index = start; index <= lastStart; index++)
        {
            if (haystack[index] == first && haystack[index + 1] == second)
            {
                return index;
            }
        }

        return -1;
    }

    private int FindLineEnd(ReadOnlySpan<byte> haystack, int start)
    {
        int offset = haystack[start..].IndexOf(lineTerminator);
        return offset < 0 ? haystack.Length : start + offset;
    }

    private int FindLineStart(ReadOnlySpan<byte> haystack, int end)
    {
        int offset = haystack[..end].LastIndexOf(lineTerminator);
        return offset < 0 ? 0 : offset + 1;
    }

    private static bool TryGetRepeatedLazyDotStarDelimiter(
        RegexSyntaxNode node,
        out byte delimiter,
        out int repeatCount)
    {
        delimiter = 0;
        repeatCount = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: { } maximum,
                Lazy: false,
            } repetition ||
            repetition.Minimum != maximum ||
            maximum > MaxRepeat ||
            !TryGetLazyDotStarDelimiter(repetition.Child, out delimiter))
        {
            return false;
        }

        repeatCount = maximum;
        return true;
    }

    private static bool TryGetLazyDotStarDelimiter(RegexSyntaxNode node, out byte delimiter)
    {
        delimiter = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            UnwrapTransparentGroups(sequence.Nodes[0]) is not RegexRepetitionNode
            {
                Child.Kind: RegexSyntaxKind.Dot,
                Minimum: 0,
                Maximum: null,
                Lazy: true,
            } ||
            !TryGetAsciiLiteral(sequence.Nodes[1], out byte[] literal) ||
            literal.Length != 1)
        {
            return false;
        }

        delimiter = literal[0];
        return true;
    }

    private static bool TryGetAsciiLiteral(RegexSyntaxNode node, out byte[] literal)
    {
        literal = [];
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom ||
            atom.Value.Length == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = atom.Value.Span;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] > 0x7F)
            {
                return false;
            }
        }

        literal = bytes.ToArray();
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
