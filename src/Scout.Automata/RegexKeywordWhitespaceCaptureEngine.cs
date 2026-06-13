using System.Buffers;

namespace Scout;

internal sealed class RegexKeywordWhitespaceCaptureEngine
{
    private readonly byte[][] keywords;
    private readonly SearchValues<byte> firstBytes;
    private readonly RegexCompileOptions options;
    private readonly int leadingCaptureIndex;
    private readonly int trailingCaptureIndex;
    private readonly int captureCount;

    private RegexKeywordWhitespaceCaptureEngine(
        byte[][] keywords,
        SearchValues<byte> firstBytes,
        RegexCompileOptions options,
        int leadingCaptureIndex,
        int trailingCaptureIndex,
        int captureCount)
    {
        this.keywords = keywords;
        this.firstBytes = firstBytes;
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
            !TryCreateFirstBytes(keywords, out SearchValues<byte>? firstBytes))
        {
            return false;
        }

        engine = new RegexKeywordWhitespaceCaptureEngine(
            keywords,
            firstBytes!,
            options,
            leadingCaptureIndex,
            trailingCaptureIndex,
            captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int search = lowerBound;
        while (search < haystack.Length)
        {
            int relative = haystack[search..].IndexOfAny(firstBytes);
            if (relative < 0)
            {
                return null;
            }

            int keywordStart = search + relative;
            if (TryFindBoundedKeywordEnd(haystack, keywordStart, out int keywordEnd))
            {
                int leadingStart = ConsumeWhitespaceBackward(haystack, lowerBound, keywordStart);
                int trailingEnd = ConsumeWhitespaceForward(haystack, keywordEnd);
                var match = new RegexMatch(leadingStart, trailingEnd - leadingStart);
                var groups = new RegexMatch?[captureCount + 1];
                groups[0] = match;
                groups[leadingCaptureIndex] = new RegexMatch(leadingStart, keywordStart - leadingStart);
                groups[trailingCaptureIndex] = new RegexMatch(keywordEnd, trailingEnd - keywordEnd);
                return new RegexCaptures(match, groups);
            }

            search = keywordStart + 1;
        }

        return null;
    }

    private bool TryFindBoundedKeywordEnd(ReadOnlySpan<byte> haystack, int start, out int end)
    {
        end = 0;
        if (!IsWordBoundaryBeforeAsciiWord(haystack, start))
        {
            return false;
        }

        for (int index = 0; index < keywords.Length; index++)
        {
            ReadOnlySpan<byte> keyword = keywords[index];
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

    private bool IsWordBoundaryBeforeAsciiWord(ReadOnlySpan<byte> haystack, int position)
    {
        if (position > 0 &&
            IsAsciiWord(haystack[position - 1]))
        {
            return false;
        }

        return IsWordBoundary(haystack, position);
    }

    private bool IsWordBoundaryAfterAsciiWord(ReadOnlySpan<byte> haystack, int position)
    {
        if (position < haystack.Length &&
            IsAsciiWord(haystack[position]))
        {
            return false;
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

    private static bool TryCreateFirstBytes(byte[][] keywords, out SearchValues<byte>? firstBytes)
    {
        firstBytes = null;
        byte[] values = new byte[keywords.Length];
        int count = 0;
        for (int index = 0; index < keywords.Length; index++)
        {
            byte first = keywords[index][0];
            bool seen = false;
            for (int valueIndex = 0; valueIndex < count; valueIndex++)
            {
                if (values[valueIndex] == first)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                values[count] = first;
                count++;
            }
        }

        if (count == 0)
        {
            return false;
        }

        firstBytes = SearchValues.Create(values.AsSpan(0, count).ToArray());
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

    private static bool IsAsciiWord(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z' ||
            value is >= (byte)'0' and <= (byte)'9' ||
            value == (byte)'_';
    }

    private static bool IsAsciiRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }
}
