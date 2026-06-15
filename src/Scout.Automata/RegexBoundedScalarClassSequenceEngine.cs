using System.Buffers;
using System.Text;

namespace Scout;

internal sealed class RegexBoundedScalarClassSequenceEngine
{
    private const int MaxRepeat = 256;

    private readonly byte startClassStart;
    private readonly byte startClassEnd;
    private readonly byte gapClassStart;
    private readonly byte gapClassEnd;
    private readonly bool gapClassNegated;
    private readonly int repeatCount;
    private readonly byte suffix;

    private RegexBoundedScalarClassSequenceEngine(
        byte startClassStart,
        byte startClassEnd,
        byte gapClassStart,
        byte gapClassEnd,
        bool gapClassNegated,
        int repeatCount,
        byte suffix)
    {
        this.startClassStart = startClassStart;
        this.startClassEnd = startClassEnd;
        this.gapClassStart = gapClassStart;
        this.gapClassEnd = gapClassEnd;
        this.gapClassNegated = gapClassNegated;
        this.repeatCount = repeatCount;
        this.suffix = suffix;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexBoundedScalarClassSequenceEngine? engine)
    {
        engine = null;
        if (!(options.Utf8 || options.UnicodeClasses) ||
            options.CaseInsensitive ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 3 } sequence ||
            !TryGetAsciiRangeClass(sequence.Nodes[0], allowNegated: false, out byte startClassStart, out byte startClassEnd, out _) ||
            !TryGetExactScalarRepetition(sequence.Nodes[1], out byte gapClassStart, out byte gapClassEnd, out bool gapClassNegated, out int repeatCount) ||
            !TryGetAsciiLiteral(sequence.Nodes[2], out byte suffix))
        {
            return false;
        }

        engine = new RegexBoundedScalarClassSequenceEngine(
            startClassStart,
            startClassEnd,
            gapClassStart,
            gapClassEnd,
            gapClassNegated,
            repeatCount,
            suffix);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int minPrefixBytes = repeatCount + 1;
        int maxPrefixBytes = (repeatCount * 4) + 1;
        int searchAt = Math.Min(haystack.Length, lowerBound + minPrefixBytes);
        RegexMatch? best = null;
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOf(suffix);
            if (offset < 0)
            {
                return best;
            }

            int suffixAt = searchAt + offset;
            if (best.HasValue && suffixAt - maxPrefixBytes > best.Value.Start)
            {
                return best;
            }

            if (TryGetStartBeforeSuffix(haystack, suffixAt, out int start) &&
                start >= lowerBound &&
                (!best.HasValue || start < best.Value.Start) &&
                TryMatchAt(haystack, start, out int length))
            {
                best = new RegexMatch(start, length);
            }

            searchAt = suffixAt + 1;
        }

        return best;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, start, out int length)
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

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            !MatchesAscii(haystack[start], startClassStart, startClassEnd, negated: false))
        {
            return false;
        }

        if (TryMatchAsciiFastPath(haystack, start, out length, out bool completed))
        {
            return true;
        }

        if (completed)
        {
            return false;
        }

        return TryMatchScalarPath(haystack, start, out length);
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

    private bool TryMatchAsciiFastPath(ReadOnlySpan<byte> haystack, int start, out int length, out bool completed)
    {
        length = 0;
        completed = false;
        int gapStart = start + 1;
        if (repeatCount > haystack.Length - gapStart - 1)
        {
            completed = true;
            return false;
        }

        for (int index = 0; index < repeatCount; index++)
        {
            byte value = haystack[gapStart + index];
            if (value > 0x7F)
            {
                return false;
            }

            if (!MatchesAscii(value, gapClassStart, gapClassEnd, gapClassNegated))
            {
                completed = true;
                return false;
            }
        }

        int suffixAt = gapStart + repeatCount;
        completed = true;
        if (haystack[suffixAt] != suffix)
        {
            return false;
        }

        length = suffixAt + 1 - start;
        return true;
    }

    private bool TryMatchScalarPath(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        int position = start + 1;
        for (int count = 0; count < repeatCount; count++)
        {
            if (position >= haystack.Length)
            {
                return false;
            }

            byte value = haystack[position];
            if (value <= 0x7F)
            {
                if (!MatchesAscii(value, gapClassStart, gapClassEnd, gapClassNegated))
                {
                    return false;
                }

                position++;
                continue;
            }

            if (!TryDecodeUtf8ScalarLength(haystack, position, out int scalarLength) ||
                !gapClassNegated)
            {
                return false;
            }

            position += scalarLength;
        }

        if (position >= haystack.Length || haystack[position] != suffix)
        {
            return false;
        }

        length = position + 1 - start;
        return true;
    }

    private static bool TryGetExactScalarRepetition(
        RegexSyntaxNode node,
        out byte classStart,
        out byte classEnd,
        out bool classNegated,
        out int repeatCount)
    {
        classStart = 0;
        classEnd = 0;
        classNegated = false;
        repeatCount = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Maximum: { } maximum,
            } repetition ||
            repetition.Minimum != maximum ||
            maximum is <= 0 or > MaxRepeat ||
            !TryGetAsciiRangeClass(repetition.Child, allowNegated: true, out classStart, out classEnd, out classNegated))
        {
            return false;
        }

        repeatCount = maximum;
        return true;
    }

    private static bool TryGetAsciiRangeClass(
        RegexSyntaxNode node,
        bool allowNegated,
        out byte classStart,
        out byte classEnd,
        out bool negated)
    {
        classStart = 0;
        classEnd = 0;
        negated = false;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom)
        {
            return false;
        }

        ReadOnlySpan<byte> expression = atom.Value.Span;
        negated = !expression.IsEmpty && expression[0] == (byte)'^';
        if (negated)
        {
            if (!allowNegated)
            {
                return false;
            }

            expression = expression[1..];
        }

        if (expression.Length != 3 ||
            expression[1] != (byte)'-' ||
            expression[0] > 0x7F ||
            expression[2] > 0x7F ||
            expression[0] > expression[2])
        {
            return false;
        }

        classStart = expression[0];
        classEnd = expression[2];
        return true;
    }

    private static bool TryGetAsciiLiteral(RegexSyntaxNode node, out byte literal)
    {
        literal = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom ||
            atom.Value.Length != 1 ||
            atom.Value.Span[0] > 0x7F)
        {
            return false;
        }

        literal = atom.Value.Span[0];
        return true;
    }

    private static bool TryDecodeUtf8ScalarLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        OperationStatus status = Rune.DecodeFromUtf8(haystack[position..], out _, out length);
        if (status == OperationStatus.Done)
        {
            return true;
        }

        length = 0;
        return false;
    }

    private static bool MatchesAscii(byte value, byte start, byte end, bool negated)
    {
        bool inRange = start <= value && value <= end;
        return negated ? !inRange : inRange;
    }

    private bool TryGetStartBeforeSuffix(ReadOnlySpan<byte> haystack, int suffixAt, out int start)
    {
        int position = suffixAt;
        for (int count = 0; count <= repeatCount; count++)
        {
            if (!TryMovePreviousScalar(haystack, position, out position))
            {
                start = 0;
                return false;
            }
        }

        start = position;
        return true;
    }

    private static bool TryMovePreviousScalar(ReadOnlySpan<byte> haystack, int position, out int previousStart)
    {
        previousStart = 0;
        if (position <= 0 || position > haystack.Length)
        {
            return false;
        }

        int asciiStart = position - 1;
        if (haystack[asciiStart] <= 0x7F)
        {
            previousStart = asciiStart;
            return true;
        }

        int lowerBound = Math.Max(0, position - 4);
        for (int candidate = asciiStart; candidate >= lowerBound; candidate--)
        {
            OperationStatus status = Rune.DecodeFromUtf8(haystack[candidate..position], out _, out int length);
            if (status == OperationStatus.Done && candidate + length == position)
            {
                previousStart = candidate;
                return true;
            }
        }

        return false;
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
