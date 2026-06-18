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
    private readonly RegexSyntaxKind suffixKind;
    private readonly byte[] suffixValue;
    private readonly byte asciiSuffix;
    private readonly bool searchByAsciiSuffix;
    private readonly byte[] suffixFirstBytes;
    private readonly SearchValues<byte>? suffixFirstByteValues;
    private readonly SearchValues<byte> startSearchValues;
    private readonly RegexCompileOptions options;

    private RegexBoundedScalarClassSequenceEngine(
        byte startClassStart,
        byte startClassEnd,
        byte gapClassStart,
        byte gapClassEnd,
        bool gapClassNegated,
        int repeatCount,
        RegexSyntaxKind suffixKind,
        byte[] suffixValue,
        byte asciiSuffix,
        bool searchByAsciiSuffix,
        byte[] suffixFirstBytes,
        SearchValues<byte>? suffixFirstByteValues,
        RegexCompileOptions options)
    {
        this.startClassStart = startClassStart;
        this.startClassEnd = startClassEnd;
        this.gapClassStart = gapClassStart;
        this.gapClassEnd = gapClassEnd;
        this.gapClassNegated = gapClassNegated;
        this.repeatCount = repeatCount;
        this.suffixKind = suffixKind;
        this.suffixValue = suffixValue;
        this.asciiSuffix = asciiSuffix;
        this.searchByAsciiSuffix = searchByAsciiSuffix;
        this.suffixFirstBytes = suffixFirstBytes;
        this.suffixFirstByteValues = suffixFirstByteValues;
        startSearchValues = SearchValues.Create(BuildRangeNeedles(startClassStart, startClassEnd));
        this.options = options;
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
            !TryGetSuffixAtom(sequence.Nodes[2], out RegexSyntaxKind suffixKind, out byte[]? suffixValue, out byte asciiSuffix, out bool searchByAsciiSuffix, out byte[]? suffixFirstBytes))
        {
            return false;
        }

        SearchValues<byte>? suffixFirstByteValues = !searchByAsciiSuffix && suffixFirstBytes.Length > 3
            ? SearchValues.Create(suffixFirstBytes)
            : null;
        engine = new RegexBoundedScalarClassSequenceEngine(
            startClassStart,
            startClassEnd,
            gapClassStart,
            gapClassEnd,
            gapClassNegated,
            repeatCount,
            suffixKind,
            suffixValue,
            asciiSuffix,
            searchByAsciiSuffix,
            suffixFirstBytes,
            suffixFirstByteValues,
            options);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (searchByAsciiSuffix)
        {
            return FindByAsciiSuffix(haystack, startAt);
        }

        if (suffixFirstBytes.Length > 0)
        {
            return FindBySuffixFirstByte(haystack, startAt);
        }

        return FindFromStart(haystack, startAt);
    }

    private RegexMatch? FindByAsciiSuffix(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int minPrefixBytes = repeatCount + 1;
        int maxPrefixBytes = (repeatCount * 4) + 1;
        int searchAt = Math.Min(haystack.Length, lowerBound + minPrefixBytes);
        RegexMatch? best = null;
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOf(asciiSuffix);
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

    private RegexMatch? FindBySuffixFirstByte(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int minPrefixBytes = repeatCount + 1;
        int maxPrefixBytes = (repeatCount * 4) + 1;
        int searchAt = Math.Min(haystack.Length, lowerBound + minPrefixBytes);
        RegexMatch? best = null;
        while (TryFindSuffixFirstByte(haystack, searchAt, out int suffixAt))
        {
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
        if (!TryMatchSuffix(haystack, suffixAt, out int suffixLength))
        {
            return false;
        }

        length = suffixAt + suffixLength - start;
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

        if (!TryMatchSuffix(haystack, position, out int suffixLength))
        {
            return false;
        }

        length = position + suffixLength - start;
        return true;
    }

    private RegexMatch? FindFromStart(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(startSearchValues);
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

    private bool TryFindSuffixFirstByte(ReadOnlySpan<byte> haystack, int startAt, out int suffixAt)
    {
        suffixAt = Math.Clamp(startAt, 0, haystack.Length);
        if (suffixAt >= haystack.Length)
        {
            suffixAt = 0;
            return false;
        }

        if (suffixFirstBytes.Length == 1)
        {
            int offset = haystack[suffixAt..].IndexOf(suffixFirstBytes[0]);
            if (offset >= 0)
            {
                suffixAt += offset;
                return true;
            }

            suffixAt = 0;
            return false;
        }

        if (suffixFirstBytes.Length == 2)
        {
            int offset = haystack[suffixAt..].IndexOfAny(suffixFirstBytes[0], suffixFirstBytes[1]);
            if (offset >= 0)
            {
                suffixAt += offset;
                return true;
            }

            suffixAt = 0;
            return false;
        }

        if (suffixFirstBytes.Length == 3)
        {
            int offset = haystack[suffixAt..].IndexOfAny(suffixFirstBytes[0], suffixFirstBytes[1], suffixFirstBytes[2]);
            if (offset >= 0)
            {
                suffixAt += offset;
                return true;
            }

            suffixAt = 0;
            return false;
        }

        if (suffixFirstByteValues is not null)
        {
            int offset = haystack[suffixAt..].IndexOfAny(suffixFirstByteValues);
            if (offset >= 0)
            {
                suffixAt += offset;
                return true;
            }

            suffixAt = 0;
            return false;
        }

        suffixAt = 0;
        return false;
    }

    private bool TryMatchSuffix(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            suffixKind,
            suffixValue,
            caseInsensitive: false,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out length);
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

    private static bool TryGetSuffixAtom(
        RegexSyntaxNode node,
        out RegexSyntaxKind suffixKind,
        out byte[] suffixValue,
        out byte asciiSuffix,
        out bool searchByAsciiSuffix,
        out byte[] suffixFirstBytes)
    {
        suffixKind = RegexSyntaxKind.Literal;
        suffixValue = [];
        asciiSuffix = 0;
        searchByAsciiSuffix = false;
        suffixFirstBytes = [];
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode atom)
        {
            return false;
        }

        if (atom.Kind == RegexSyntaxKind.Literal)
        {
            suffixKind = atom.Kind;
            suffixValue = atom.Value.ToArray();
            searchByAsciiSuffix = suffixValue.Length == 1 && suffixValue[0] <= 0x7F;
            asciiSuffix = searchByAsciiSuffix ? suffixValue[0] : (byte)0;
            suffixFirstBytes = suffixValue.Length > 0 ? [suffixValue[0]] : [];
            return suffixValue.Length > 0;
        }

        if (atom.Kind != RegexSyntaxKind.CharacterClass ||
            !TryGetSimpleLatin1ScalarClassFirstBytes(atom.Value.Span, out suffixFirstBytes))
        {
            return false;
        }

        suffixKind = atom.Kind;
        suffixValue = atom.Value.ToArray();
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

    private static byte[] BuildRangeNeedles(byte start, byte end)
    {
        byte[] needles = new byte[end - start + 1];
        for (int value = start; value <= end; value++)
        {
            needles[value - start] = (byte)value;
        }

        return needles;
    }

    private static bool TryGetSimpleLatin1ScalarClassFirstBytes(ReadOnlySpan<byte> expression, out byte[] firstBytes)
    {
        firstBytes = [];
        if (expression.IsEmpty ||
            RegexByteClass.IsNegatedClass(expression) ||
            RegexByteClass.TryFindClassIntersectionOperator(expression, out _) ||
            !IsAscii(expression))
        {
            return false;
        }

        int index = 0;
        bool[] candidates = new bool[256];
        while (index < expression.Length)
        {
            if (!RegexByteClass.TryReadClassToken(expression, ref index, out RegexSyntaxKind tokenKind, out byte literal, out bool tokenNegated) ||
                tokenKind != RegexSyntaxKind.Literal ||
                tokenNegated)
            {
                return false;
            }

            if (index + 1 < expression.Length && expression[index] == (byte)'-')
            {
                int rangeEndIndex = index + 1;
                if (!RegexByteClass.TryReadClassToken(expression, ref rangeEndIndex, out RegexSyntaxKind rangeEndKind, out byte rangeEnd, out bool rangeEndNegated) ||
                    rangeEndKind != RegexSyntaxKind.Literal ||
                    rangeEndNegated)
                {
                    return false;
                }

                if (literal > rangeEnd)
                {
                    return false;
                }

                AddLatin1ScalarFirstBytes(candidates, literal, rangeEnd);
                index = rangeEndIndex;
                continue;
            }

            AddLatin1ScalarFirstBytes(candidates, literal, literal);
        }

        firstBytes = BuildNeedles(candidates);
        return firstBytes.Length > 0;
    }

    private static bool IsAscii(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddLatin1ScalarFirstBytes(bool[] firstBytes, byte start, byte end)
    {
        for (int scalar = start; scalar <= end; scalar++)
        {
            firstBytes[GetUtf8FirstByte(scalar)] = true;
        }
    }

    private static byte GetUtf8FirstByte(int scalar)
    {
        if (scalar <= 0x7F)
        {
            return (byte)scalar;
        }

        return (byte)(0xC0 | (scalar >> 6));
    }

    private static byte[] BuildNeedles(bool[] bytes)
    {
        int count = 0;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (bytes[value])
            {
                count++;
            }
        }

        byte[] needles = new byte[count];
        int write = 0;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (bytes[value])
            {
                needles[write++] = (byte)value;
            }
        }

        return needles;
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
