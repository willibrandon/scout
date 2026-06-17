using System.Runtime.CompilerServices;

namespace Scout;

internal sealed class RegexKeywordWhitespaceCaptureEngine
{
    private readonly byte[][][] keywordsByFirstByte;
    private readonly bool[] firstByteLookup;
    private readonly RegexCompileOptions options;
    private readonly int leadingCaptureIndex;
    private readonly int trailingCaptureIndex;
    private readonly int captureCount;

    private RegexKeywordWhitespaceCaptureEngine(
        byte[][][] keywordsByFirstByte,
        bool[] firstByteLookup,
        RegexCompileOptions options,
        int leadingCaptureIndex,
        int trailingCaptureIndex,
        int captureCount)
    {
        this.keywordsByFirstByte = keywordsByFirstByte;
        this.firstByteLookup = firstByteLookup;
        this.options = options;
        this.leadingCaptureIndex = leadingCaptureIndex;
        this.trailingCaptureIndex = trailingCaptureIndex;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexKeywordWhitespaceCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 ||
            options.CaseInsensitive ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 5 } sequence ||
            !TryGetWhitespaceCapture(sequence.Nodes[0], out int leadingCaptureIndex) ||
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[1]) is not RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary } ||
            !TryGetLiteralAlternation(sequence.Nodes[2], out byte[][] keywords) ||
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[3]) is not RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary } ||
            !TryGetWhitespaceCapture(sequence.Nodes[4], out int trailingCaptureIndex))
        {
            return false;
        }

        if (keywords.Length == 0 ||
            !TryCreateKeywordBuckets(
                keywords,
                out byte[][][]? keywordsByFirstByte,
                out bool[]? firstByteLookup))
        {
            return false;
        }

        engine = new RegexKeywordWhitespaceCaptureEngine(
            keywordsByFirstByte!,
            firstByteLookup!,
            options,
            leadingCaptureIndex,
            trailingCaptureIndex,
            captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!TryFindCaptureSpan(
            haystack,
            startAt,
            out int leadingStart,
            out int keywordStart,
            out int keywordEnd,
            out int trailingEnd))
        {
            return null;
        }

        var match = new RegexMatch(leadingStart, trailingEnd - leadingStart);
        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match;
        groups[leadingCaptureIndex] = new RegexMatch(leadingStart, keywordStart - leadingStart);
        groups[trailingCaptureIndex] = new RegexMatch(keywordEnd, trailingEnd - keywordEnd);
        return new RegexCaptures(match, groups);
    }

    public long CountCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (offset < haystack.Length)
        {
            if (!TryFindKeywordEnd(haystack, offset, out int keywordEnd))
            {
                return total;
            }

            total += 3;
            offset = keywordEnd;
        }

        return total;
    }

    private bool TryFindKeywordEnd(ReadOnlySpan<byte> haystack, int startAt, out int keywordEnd)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int search = lowerBound;
        while (search < haystack.Length)
        {
            int keywordStart = FindKeywordStart(haystack, search);
            if (keywordStart < 0)
            {
                keywordEnd = 0;
                return false;
            }

            if (TryFindBoundedKeywordEnd(haystack, keywordStart, out keywordEnd))
            {
                return true;
            }

            search = keywordStart + 1;
        }

        keywordEnd = 0;
        return false;
    }

    private bool TryFindCaptureSpan(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out int leadingStart,
        out int keywordStart,
        out int keywordEnd,
        out int trailingEnd)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int search = lowerBound;
        while (search < haystack.Length)
        {
            keywordStart = FindKeywordStart(haystack, search);
            if (keywordStart < 0)
            {
                leadingStart = 0;
                keywordEnd = 0;
                trailingEnd = 0;
                return false;
            }

            if (TryFindBoundedKeywordEnd(haystack, keywordStart, out keywordEnd))
            {
                leadingStart = ConsumeWhitespaceBackward(haystack, lowerBound, keywordStart);
                trailingEnd = ConsumeWhitespaceForward(haystack, keywordEnd);
                return true;
            }

            search = keywordStart + 1;
        }

        leadingStart = 0;
        keywordStart = 0;
        keywordEnd = 0;
        trailingEnd = 0;
        return false;
    }

    private bool TryFindBoundedKeywordEnd(ReadOnlySpan<byte> haystack, int start, out int end)
    {
        end = 0;
        byte[][] candidates = keywordsByFirstByte[haystack[start]];
        for (int index = 0; index < candidates.Length; index++)
        {
            ReadOnlySpan<byte> keyword = candidates[index];
            if (keyword.Length > haystack.Length - start ||
                !haystack.Slice(start, keyword.Length).SequenceEqual(keyword))
            {
                continue;
            }

            int candidateEnd = start + keyword.Length;
            if (!IsWordBoundaryAfterAsciiWord(haystack, candidateEnd))
            {
                continue;
            }

            end = candidateEnd;
            return true;
        }

        return false;
    }

    private int FindKeywordStart(ReadOnlySpan<byte> haystack, int start)
    {
        int position = start;
        while (position < haystack.Length)
        {
            byte value = haystack[position];
            if (value <= 0x7F && IsAsciiWord(value))
            {
                if (firstByteLookup[value] &&
                    IsWordBoundaryBeforeAsciiWord(haystack, position))
                {
                    return position;
                }

                position = SkipAsciiWord(haystack, position + 1);
                continue;
            }

            if (firstByteLookup[value] &&
                IsWordBoundaryBeforeAsciiWord(haystack, position))
            {
                return position;
            }

            position++;
        }

        return -1;
    }

    private static int SkipAsciiWord(ReadOnlySpan<byte> haystack, int position)
    {
        while (position < haystack.Length &&
            haystack[position] <= 0x7F &&
            IsAsciiWord(haystack[position]))
        {
            position++;
        }

        return position;
    }

    private bool IsWordBoundaryBeforeAsciiWord(ReadOnlySpan<byte> haystack, int position)
    {
        if (position == 0)
        {
            return true;
        }

        byte previous = haystack[position - 1];
        if (previous <= 0x7F)
        {
            return !IsAsciiWord(previous);
        }

        return IsWordBoundary(haystack, position);
    }

    private bool IsWordBoundaryAfterAsciiWord(ReadOnlySpan<byte> haystack, int position)
    {
        if (position >= haystack.Length)
        {
            return true;
        }

        byte next = haystack[position];
        if (next <= 0x7F)
        {
            return !IsAsciiWord(next);
        }

        return IsWordBoundary(haystack, position);
    }

    private bool IsWordBoundary(ReadOnlySpan<byte> haystack, int position)
    {
        return RegexByteClass.PredicateMatches(
            haystack,
            position,
            RegexSyntaxKind.WordBoundary,
            options.MultiLine,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses);
    }

    private int ConsumeWhitespaceForward(ReadOnlySpan<byte> haystack, int position)
    {
        while (TryWhitespaceMatchLength(haystack, position, out int length))
        {
            position += length;
        }

        return position;
    }

    private int ConsumeWhitespaceBackward(ReadOnlySpan<byte> haystack, int lowerBound, int end)
    {
        int position = end;
        while (position > lowerBound &&
            TryWhitespaceEndingAt(haystack, lowerBound, position, out int start))
        {
            position = start;
        }

        return position;
    }

    private bool TryWhitespaceEndingAt(ReadOnlySpan<byte> haystack, int lowerBound, int end, out int start)
    {
        start = 0;
        int previous = end - 1;
        if (haystack[previous] <= 0x7F)
        {
            if (!IsAsciiRegexWhitespace(haystack[previous]))
            {
                return false;
            }

            start = previous;
            return true;
        }

        int firstCandidate = Math.Max(lowerBound, end - 4);
        for (int candidate = firstCandidate; candidate < end; candidate++)
        {
            if (TryWhitespaceMatchLength(haystack, candidate, out int length) &&
                candidate + length == end)
            {
                start = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryWhitespaceMatchLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        if ((uint)position >= (uint)haystack.Length)
        {
            return false;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            if (!IsAsciiRegexWhitespace(first))
            {
                return false;
            }

            length = 1;
            return true;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            RegexSyntaxKind.WhitespaceClass,
            ReadOnlySpan<byte>.Empty,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out length);
    }

    private static bool TryGetWhitespaceCapture(RegexSyntaxNode node, out int captureIndex)
    {
        captureIndex = 0;
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group ||
            UnwrapTransparentNonCapturingGroups(group.Child) is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: null,
                Lazy: false,
            } repetition ||
            UnwrapTransparentNonCapturingGroups(repetition.Child) is not RegexAtomNode { Kind: RegexSyntaxKind.WhitespaceClass })
        {
            return false;
        }

        captureIndex = group.CaptureIndex;
        return true;
    }

    private static bool TryGetLiteralAlternation(RegexSyntaxNode node, out byte[][] literals)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexAlternationNode alternation)
        {
            literals = new byte[alternation.Alternatives.Count][];
            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryGetLiteral(alternation.Alternatives[index], out literals[index]))
                {
                    literals = [];
                    return false;
                }
            }

            return literals.Length != 0;
        }

        if (!TryGetLiteral(node, out byte[] literal))
        {
            literals = [];
            return false;
        }

        literals = [literal];
        return true;
    }

    private static bool TryGetLiteral(RegexSyntaxNode node, out byte[] literal)
    {
        literal = [];
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
        {
            literal = atom.Value.ToArray();
            return literal.Length != 0;
        }

        if (node is not RegexSequenceNode sequence)
        {
            return false;
        }

        var bytes = new List<byte>();
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            if (UnwrapTransparentNonCapturingGroups(sequence.Nodes[index]) is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } literalAtom)
            {
                return false;
            }

            bytes.AddRange(literalAtom.Value.Span.ToArray());
        }

        literal = bytes.ToArray();
        return literal.Length != 0;
    }

    private static bool TryCreateKeywordBuckets(
        byte[][] keywords,
        out byte[][][]? keywordsByFirstByte,
        out bool[]? firstByteLookup)
    {
        keywordsByFirstByte = null;
        firstByteLookup = null;
        var buckets = new List<byte[]>[256];
        int count = 0;
        for (int index = 0; index < keywords.Length; index++)
        {
            byte first = keywords[index][0];
            (buckets[first] ??= []).Add(keywords[index]);
            if (buckets[first]!.Count == 1)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return false;
        }

        keywordsByFirstByte = new byte[256][][];
        for (int index = 0; index < buckets.Length; index++)
        {
            keywordsByFirstByte[index] = buckets[index]?.ToArray() ?? [];
        }

        firstByteLookup = new bool[256];
        for (int index = 0; index < buckets.Length; index++)
        {
            firstByteLookup[index] = buckets[index] is not null;
        }

        return true;
    }

    private static RegexSyntaxNode UnwrapTransparentNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.NonCapturingGroup,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group)
        {
            node = group.Child;
        }

        return node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiWord(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z' ||
            value is >= (byte)'0' and <= (byte)'9' ||
            value == (byte)'_';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }
}
