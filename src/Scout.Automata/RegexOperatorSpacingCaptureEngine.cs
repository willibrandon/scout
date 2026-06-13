using System.Buffers;

namespace Scout;

internal sealed class RegexOperatorSpacingCaptureEngine
{
    private static readonly SearchValues<byte> OperatorCandidateBytes = SearchValues.Create("-+*/|!<=>%&^:"u8);

    private readonly RegexCompileOptions options;
    private readonly ReadOnlyMemory<byte> firstClassExpression;
    private readonly int leadingCaptureIndex;
    private readonly int trailingCaptureIndex;
    private readonly int captureCount;

    private RegexOperatorSpacingCaptureEngine(
        RegexCompileOptions options,
        ReadOnlyMemory<byte> firstClassExpression,
        int leadingCaptureIndex,
        int trailingCaptureIndex,
        int captureCount)
    {
        this.options = options;
        this.firstClassExpression = firstClassExpression;
        this.leadingCaptureIndex = leadingCaptureIndex;
        this.trailingCaptureIndex = trailingCaptureIndex;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexOperatorSpacingCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 ||
            options.CaseInsensitive ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 4 } sequence ||
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[0]) is not RegexAtomNode
            {
                Kind: RegexSyntaxKind.CharacterClass,
            } firstClass ||
            !firstClass.Value.Span.SequenceEqual("^,\\s"u8) ||
            !TryGetWhitespaceCapture(sequence.Nodes[1], out int leadingCaptureIndex) ||
            !TryGetOperatorAlternation(sequence.Nodes[2]) ||
            !TryGetWhitespaceCapture(sequence.Nodes[3], out int trailingCaptureIndex))
        {
            return false;
        }

        engine = new RegexOperatorSpacingCaptureEngine(
            options,
            firstClass.Value.ToArray(),
            leadingCaptureIndex,
            trailingCaptureIndex,
            captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int minimumStart = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = minimumStart;
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(OperatorCandidateBytes);
            if (offset < 0)
            {
                return null;
            }

            int operatorStart = searchAt + offset;
            if (!TryConsumeOperator(haystack, operatorStart, out int operatorEnd))
            {
                searchAt = operatorStart + 1;
                continue;
            }

            int leadingStart = FindLeadingWhitespaceStart(haystack, operatorStart);
            if (TryPreviousFirstAtomMatchStart(haystack, leadingStart, out int matchStart) &&
                matchStart >= minimumStart)
            {
                int trailingEnd = ConsumeWhitespaceForward(haystack, operatorEnd);
                var match = new RegexMatch(matchStart, trailingEnd - matchStart);
                var groups = new RegexMatch?[captureCount + 1];
                groups[0] = match;
                groups[leadingCaptureIndex] = new RegexMatch(leadingStart, operatorStart - leadingStart);
                groups[trailingCaptureIndex] = new RegexMatch(operatorEnd, trailingEnd - operatorEnd);
                return new RegexCaptures(match, groups);
            }

            searchAt = operatorStart + 1;
        }

        return null;
    }

    private int FindLeadingWhitespaceStart(ReadOnlySpan<byte> haystack, int operatorStart)
    {
        int position = operatorStart;
        while (TryPreviousWhitespaceMatchStart(haystack, position, out int whitespaceStart))
        {
            position = whitespaceStart;
        }

        return position;
    }

    private bool TryPreviousWhitespaceMatchStart(ReadOnlySpan<byte> haystack, int end, out int start)
    {
        start = 0;
        if (end <= 0)
        {
            return false;
        }

        byte last = haystack[end - 1];
        if (last <= 0x7F)
        {
            if (!IsAsciiRegexWhitespace(last))
            {
                return false;
            }

            start = end - 1;
            return true;
        }

        int firstCandidate = Math.Max(0, end - 4);
        for (int candidate = end - 1; candidate >= firstCandidate; candidate--)
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

    private bool TryPreviousFirstAtomMatchStart(ReadOnlySpan<byte> haystack, int end, out int start)
    {
        start = 0;
        if (end <= 0)
        {
            return false;
        }

        byte last = haystack[end - 1];
        if (last <= 0x7F)
        {
            if (last == (byte)',' || IsAsciiRegexWhitespace(last))
            {
                return false;
            }

            start = end - 1;
            return true;
        }

        int firstCandidate = Math.Max(0, end - 4);
        for (int candidate = end - 1; candidate >= firstCandidate; candidate--)
        {
            if (TryFirstAtomMatchLength(haystack, candidate, out int length) &&
                candidate + length == end)
            {
                start = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryFirstAtomMatchLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        if ((uint)position >= (uint)haystack.Length)
        {
            return false;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            if (first == (byte)',' ||
                IsAsciiRegexWhitespace(first))
            {
                return false;
            }

            length = 1;
            return true;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            RegexSyntaxKind.CharacterClass,
            firstClassExpression.Span,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out length);
    }

    private int ConsumeWhitespaceForward(ReadOnlySpan<byte> haystack, int position)
    {
        while (TryWhitespaceMatchLength(haystack, position, out int length))
        {
            position += length;
        }

        return position;
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

    private static bool TryConsumeOperator(ReadOnlySpan<byte> haystack, int position, out int end)
    {
        end = position;
        if ((uint)position >= (uint)haystack.Length)
        {
            return false;
        }

        if (IsOperatorByte(haystack[position]))
        {
            end = position + 1;
            while (end < haystack.Length && IsOperatorByte(haystack[end]))
            {
                end++;
            }

            return true;
        }

        if (position + 1 < haystack.Length &&
            haystack[position] == (byte)':' &&
            haystack[position + 1] == (byte)'=')
        {
            end = position + 2;
            return true;
        }

        return false;
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

    private static bool TryGetOperatorAlternation(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexAlternationNode { Alternatives.Count: 2 } alternation)
        {
            return false;
        }

        bool sawOperatorRun = false;
        bool sawColonEquals = false;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            RegexSyntaxNode alternative = UnwrapTransparentNonCapturingGroups(alternation.Alternatives[index]);
            if (alternative is RegexRepetitionNode
                {
                    Minimum: 1,
                    Maximum: null,
                    Lazy: false,
                } repetition &&
                UnwrapTransparentNonCapturingGroups(repetition.Child) is RegexAtomNode
                {
                    Kind: RegexSyntaxKind.CharacterClass,
                } operatorClass &&
                operatorClass.Value.Span.SequenceEqual("-+*/|!<=>%&^"u8))
            {
                sawOperatorRun = true;
                continue;
            }

            if (TryGetLiteral(alternative, out byte[] literal) &&
                literal.AsSpan().SequenceEqual(":="u8))
            {
                sawColonEquals = true;
                continue;
            }

            return false;
        }

        return sawOperatorRun && sawColonEquals;
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

    private static bool IsOperatorByte(byte value)
    {
        return value is (byte)'-' or (byte)'+' or (byte)'*' or (byte)'/' or (byte)'|' or (byte)'!' or
            (byte)'<' or (byte)'=' or (byte)'>' or (byte)'%' or (byte)'&' or (byte)'^';
    }

    private static bool IsAsciiRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }
}
