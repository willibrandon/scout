namespace Scout;

internal sealed class RegexBoundedByteClassSequenceEngine
{
    private const int MaxRepeat = 256;
    private const int MaxStartBytes = 8;

    private readonly bool[] startBytes;
    private readonly bool[] gapBytes;
    private readonly bool[] suffixBytes;
    private readonly bool[] endBytes;
    private readonly byte[] startNeedles;
    private readonly int minimum;
    private readonly int maximum;
    private readonly bool lazy;

    private RegexBoundedByteClassSequenceEngine(
        bool[] startBytes,
        bool[] gapBytes,
        bool[] suffixBytes,
        bool[] endBytes,
        byte[] startNeedles,
        int minimum,
        int maximum,
        bool lazy)
    {
        this.startBytes = startBytes;
        this.gapBytes = gapBytes;
        this.suffixBytes = suffixBytes;
        this.endBytes = endBytes;
        this.startNeedles = startNeedles;
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
        if (root is not RegexSequenceNode { Nodes.Count: 4 } sequence ||
            !TryGetByteAtom(sequence.Nodes[0], options, out bool[]? startBytes, out int startCount) ||
            startCount == 0 ||
            startCount > MaxStartBytes ||
            !TryGetBoundedByteRepetition(sequence.Nodes[1], options, out bool[]? gapBytes, out int minimum, out int maximum, out bool lazy) ||
            !TryGetByteAtom(sequence.Nodes[2], options, out bool[]? suffixBytes, out _) ||
            !TryGetByteAtom(sequence.Nodes[3], options, out bool[]? endBytes, out _))
        {
            return false;
        }

        byte[] startNeedles = BuildNeedles(startBytes, startCount);
        engine = new RegexBoundedByteClassSequenceEngine(
            startBytes,
            gapBytes,
            suffixBytes,
            endBytes,
            startNeedles,
            minimum,
            maximum,
            lazy);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
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
        int maxCandidate = Math.Min(maximum, haystack.Length - gapStart - 2);
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

    private bool TryAcceptSuffix(
        ReadOnlySpan<byte> haystack,
        int start,
        int gapStart,
        int gapLength,
        out int length)
    {
        int suffixAt = gapStart + gapLength;
        int endAt = suffixAt + 1;
        if (suffixBytes[haystack[suffixAt]] && endBytes[haystack[endAt]])
        {
            length = endAt + 1 - start;
            return true;
        }

        length = 0;
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
