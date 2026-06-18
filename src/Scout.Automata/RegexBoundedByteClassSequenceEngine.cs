using System.Buffers;

namespace Scout;

internal sealed class RegexBoundedByteClassSequenceEngine
{
    private const int MaxRepeat = 256;
    private const int MaxStartBytes = 64;
    private const int MaxSuffixBytes = 64;

    private readonly bool[] startBytes;
    private readonly bool[] gapBytes;
    private readonly bool[] suffixBytes;
    private readonly bool[]? endBytes;
    private readonly byte[] startNeedles;
    private readonly SearchValues<byte>? startNeedleValues;
    private readonly byte[] suffixNeedles;
    private readonly SearchValues<byte>? suffixNeedleValues;
    private readonly bool searchFromSuffix;
    private readonly int minimum;
    private readonly int maximum;
    private readonly bool lazy;

    private RegexBoundedByteClassSequenceEngine(
        bool[] startBytes,
        bool[] gapBytes,
        bool[] suffixBytes,
        bool[]? endBytes,
        byte[] startNeedles,
        SearchValues<byte>? startNeedleValues,
        byte[] suffixNeedles,
        SearchValues<byte>? suffixNeedleValues,
        bool searchFromSuffix,
        int minimum,
        int maximum,
        bool lazy)
    {
        this.startBytes = startBytes;
        this.gapBytes = gapBytes;
        this.suffixBytes = suffixBytes;
        this.endBytes = endBytes;
        this.startNeedles = startNeedles;
        this.startNeedleValues = startNeedleValues;
        this.suffixNeedles = suffixNeedles;
        this.suffixNeedleValues = suffixNeedleValues;
        this.searchFromSuffix = searchFromSuffix;
        this.minimum = minimum;
        this.maximum = maximum;
        this.lazy = lazy;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexBoundedByteClassSequenceEngine? engine)
    {
        engine = null;
        if (options.Utf8 ||
            options.UnicodeClasses ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode sequence ||
            sequence.Nodes.Count is not (3 or 4) ||
            !TryGetByteAtom(sequence.Nodes[0], options, out bool[]? startBytes, out int startCount) ||
            startCount == 0 ||
            startCount > MaxStartBytes ||
            !TryGetBoundedByteRepetition(sequence.Nodes[1], options, out bool[]? gapBytes, out int minimum, out int maximum, out bool lazy) ||
            !TryGetByteAtom(sequence.Nodes[2], options, out bool[]? suffixBytes, out int suffixCount))
        {
            return false;
        }

        if (sequence.Nodes.Count == 3 && IsLiteralAtom(sequence.Nodes[2]))
        {
            return false;
        }

        bool[]? endBytes = null;
        if (sequence.Nodes.Count == 4 &&
            !TryGetByteAtom(sequence.Nodes[3], options, out endBytes, out _))
        {
            return false;
        }

        byte[] startNeedles = BuildNeedles(startBytes, startCount);
        SearchValues<byte>? startNeedleValues = startNeedles.Length > 3
            ? SearchValues.Create(startNeedles)
            : null;
        bool searchFromSuffix = sequence.Nodes.Count == 3 &&
            minimum == maximum &&
            suffixCount <= MaxSuffixBytes;
        byte[] suffixNeedles = searchFromSuffix ? BuildNeedles(suffixBytes, suffixCount) : [];
        SearchValues<byte>? suffixNeedleValues = searchFromSuffix && suffixNeedles.Length > 3
            ? SearchValues.Create(suffixNeedles)
            : null;
        engine = new RegexBoundedByteClassSequenceEngine(
            startBytes,
            gapBytes,
            suffixBytes,
            endBytes,
            startNeedles,
            startNeedleValues,
            suffixNeedles,
            suffixNeedleValues,
            searchFromSuffix,
            minimum,
            maximum,
            lazy);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (searchFromSuffix)
        {
            return FindFromSuffix(haystack, startAt);
        }

        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindStart(haystack, searchAt, out int start))
        {
            if (TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }

            searchAt = start + 1;
        }

        return null;
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
            !startBytes[haystack[start]])
        {
            return false;
        }

        int gapStart = start + 1;
        int trailingBytes = endBytes is null ? 1 : 2;
        int maxCandidate = Math.Min(maximum, haystack.Length - gapStart - trailingBytes);
        if (maxCandidate < minimum)
        {
            return false;
        }

        int validGapLength = 0;
        while (validGapLength < maxCandidate &&
            gapBytes[haystack[gapStart + validGapLength]])
        {
            validGapLength++;
        }

        maxCandidate = Math.Min(maxCandidate, validGapLength);
        if (maxCandidate < minimum)
        {
            return false;
        }

        if (lazy)
        {
            for (int gapLength = minimum; gapLength <= maxCandidate; gapLength++)
            {
                if (TryAcceptSuffix(haystack, start, gapStart, gapLength, out length))
                {
                    return true;
                }
            }
        }
        else
        {
            for (int gapLength = maxCandidate; gapLength >= minimum; gapLength--)
            {
                if (TryAcceptSuffix(haystack, start, gapStart, gapLength, out length))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        if (searchFromSuffix)
        {
            while (FindFromSuffix(haystack, offset) is RegexMatch match)
            {
                total += sumSpans ? match.Length : 1;
                offset = match.End;
            }

            return total;
        }

        while (TryFindStart(haystack, offset, out int start))
        {
            if (TryMatchAt(haystack, start, out int length))
            {
                total += sumSpans ? length : 1;
                offset = start + length;
            }
            else
            {
                offset = start + 1;
            }
        }

        return total;
    }

    private RegexMatch? FindFromSuffix(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = Math.Min(haystack.Length, lowerBound + minimum + 1);
        while (TryFindSuffix(haystack, searchAt, out int suffixAt))
        {
            int start = suffixAt - minimum - 1;
            if (start >= lowerBound &&
                TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }

            searchAt = suffixAt + 1;
        }

        return null;
    }

    private bool TryAcceptSuffix(
        ReadOnlySpan<byte> haystack,
        int start,
        int gapStart,
        int gapLength,
        out int length)
    {
        int suffixAt = gapStart + gapLength;
        if (!suffixBytes[haystack[suffixAt]])
        {
            length = 0;
            return false;
        }

        if (endBytes is null)
        {
            length = suffixAt + 1 - start;
            return true;
        }

        int endAt = suffixAt + 1;
        if (!endBytes[haystack[endAt]])
        {
            length = 0;
            return false;
        }

        length = endAt + 1 - start;
        return true;
    }

    private bool TryFindSuffix(ReadOnlySpan<byte> haystack, int startAt, out int suffixAt)
    {
        suffixAt = Math.Clamp(startAt, 0, haystack.Length);
        if (suffixAt >= haystack.Length)
        {
            suffixAt = 0;
            return false;
        }

        if (suffixNeedles.Length == 1)
        {
            int offset = haystack[suffixAt..].IndexOf(suffixNeedles[0]);
            if (offset >= 0)
            {
                suffixAt += offset;
                return true;
            }

            suffixAt = 0;
            return false;
        }

        if (suffixNeedles.Length == 2)
        {
            int offset = haystack[suffixAt..].IndexOfAny(suffixNeedles[0], suffixNeedles[1]);
            if (offset >= 0)
            {
                suffixAt += offset;
                return true;
            }

            suffixAt = 0;
            return false;
        }

        if (suffixNeedles.Length == 3)
        {
            int offset = haystack[suffixAt..].IndexOfAny(suffixNeedles[0], suffixNeedles[1], suffixNeedles[2]);
            if (offset >= 0)
            {
                suffixAt += offset;
                return true;
            }

            suffixAt = 0;
            return false;
        }

        if (suffixNeedleValues is not null)
        {
            int offset = haystack[suffixAt..].IndexOfAny(suffixNeedleValues);
            if (offset >= 0)
            {
                suffixAt += offset;
                return true;
            }

            suffixAt = 0;
            return false;
        }

        while (suffixAt < haystack.Length)
        {
            if (suffixBytes[haystack[suffixAt]])
            {
                return true;
            }

            suffixAt++;
        }

        suffixAt = 0;
        return false;
    }

    private bool TryFindStart(ReadOnlySpan<byte> haystack, int startAt, out int start)
    {
        start = Math.Clamp(startAt, 0, haystack.Length);
        if (start >= haystack.Length)
        {
            start = 0;
            return false;
        }

        if (startNeedles.Length == 1)
        {
            int offset = haystack[start..].IndexOf(startNeedles[0]);
            if (offset >= 0)
            {
                start += offset;
                return true;
            }

            start = 0;
            return false;
        }

        if (startNeedles.Length == 2)
        {
            int offset = haystack[start..].IndexOfAny(startNeedles[0], startNeedles[1]);
            if (offset >= 0)
            {
                start += offset;
                return true;
            }

            start = 0;
            return false;
        }

        if (startNeedles.Length == 3)
        {
            int offset = haystack[start..].IndexOfAny(startNeedles[0], startNeedles[1], startNeedles[2]);
            if (offset >= 0)
            {
                start += offset;
                return true;
            }

            start = 0;
            return false;
        }

        if (startNeedleValues is not null)
        {
            int offset = haystack[start..].IndexOfAny(startNeedleValues);
            if (offset >= 0)
            {
                start += offset;
                return true;
            }

            start = 0;
            return false;
        }

        while (start < haystack.Length)
        {
            if (startBytes[haystack[start]])
            {
                return true;
            }

            start++;
        }

        start = 0;
        return false;
    }

    private static bool TryGetBoundedByteRepetition(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out bool[] bytes,
        out int minimum,
        out int maximum,
        out bool lazy)
    {
        bytes = [];
        minimum = 0;
        maximum = 0;
        lazy = false;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: >= 0,
                Maximum: { } candidateMaximum,
            } repetition ||
            candidateMaximum > MaxRepeat ||
            candidateMaximum < repetition.Minimum ||
            !TryGetByteAtom(repetition.Child, options, out bytes, out _))
        {
            return false;
        }

        minimum = repetition.Minimum;
        maximum = candidateMaximum;
        lazy = repetition.Lazy;
        return true;
    }

    private static bool TryGetByteAtom(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out bool[] bytes,
        out int count)
    {
        bytes = [];
        count = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode atom ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        bytes = new bool[256];
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (RegexByteClass.AtomMatches(
                    (byte)value,
                    atom.Kind,
                    atom.Value.Span,
                    options.CaseInsensitive,
                    options.MultiLine,
                    options.DotMatchesNewline,
                    options.Crlf,
                    options.LineTerminator))
            {
                bytes[value] = true;
                count++;
            }
        }

        return count > 0;
    }

    private static bool IsLiteralAtom(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.Literal };
    }

    private static byte[] BuildNeedles(bool[] bytes, int count)
    {
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
