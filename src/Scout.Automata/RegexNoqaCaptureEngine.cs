using System.Runtime.CompilerServices;

namespace Scout;

internal sealed class RegexNoqaCaptureEngine
{
    private static readonly byte[] NoqaLiteral = "# noqa"u8.ToArray();

    private readonly RegexAsciiCaseInsensitiveFinder finder = new(NoqaLiteral);
    private readonly RegexCompileOptions options;
    private readonly int spacesCaptureIndex;
    private readonly int noqaCaptureIndex;
    private readonly int codesCaptureIndex;
    private readonly int codeCaptureIndex;
    private readonly int captureCount;

    private RegexNoqaCaptureEngine(
        RegexCompileOptions options,
        int spacesCaptureIndex,
        int noqaCaptureIndex,
        int codesCaptureIndex,
        int codeCaptureIndex,
        int captureCount)
    {
        this.options = options;
        this.spacesCaptureIndex = spacesCaptureIndex;
        this.noqaCaptureIndex = noqaCaptureIndex;
        this.codesCaptureIndex = codesCaptureIndex;
        this.codeCaptureIndex = codeCaptureIndex;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexNoqaCaptureEngine? engine)
    {
        engine = null;
        if (captureCount < 4 ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            !TryGetWhitespaceCapture(sequence.Nodes[0], out int spacesCaptureIndex) ||
            !TryGetNoqaCapture(
                sequence.Nodes[1],
                options,
                out int noqaCaptureIndex,
                out int codesCaptureIndex,
                out int codeCaptureIndex))
        {
            return false;
        }

        engine = new RegexNoqaCaptureEngine(
            options,
            spacesCaptureIndex,
            noqaCaptureIndex,
            codesCaptureIndex,
            codeCaptureIndex,
            captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!TryFindMatch(
            haystack,
            startAt,
            out int matchStart,
            out int literalStart,
            out int matchEnd,
            out int codesStart,
            out int codesEnd,
            out int codeStart,
            out int codeEnd,
            out bool hasCodes))
        {
            return null;
        }

        var match = new RegexMatch(matchStart, matchEnd - matchStart);
        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match;
        groups[spacesCaptureIndex] = new RegexMatch(matchStart, literalStart - matchStart);
        groups[noqaCaptureIndex] = new RegexMatch(literalStart, matchEnd - literalStart);
        if (hasCodes)
        {
            groups[codesCaptureIndex] = new RegexMatch(codesStart, codesEnd - codesStart);
            groups[codeCaptureIndex] = new RegexMatch(codeStart, codeEnd - codeStart);
        }

        return new RegexCaptures(match, groups);
    }

    public long CountCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (offset < haystack.Length)
        {
            int relative = finder.Find(haystack[offset..]);
            if (relative < 0)
            {
                return total;
            }

            int literalStart = offset + relative;
            int literalEnd = literalStart + NoqaLiteral.Length;
            if (TryConsumeCodesSuffix(
                haystack,
                literalEnd,
                out _,
                out int codesEnd,
                out _,
                out _))
            {
                total += 5;
                offset = codesEnd;
                continue;
            }

            total += 3;
            offset = literalEnd;
        }

        return total;
    }

    private bool TryFindMatch(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out int matchStart,
        out int literalStart,
        out int matchEnd,
        out int codesStart,
        out int codesEnd,
        out int codeStart,
        out int codeEnd,
        out bool hasCodes)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int relative = finder.Find(haystack[lowerBound..]);
        if (relative < 0)
        {
            matchStart = 0;
            literalStart = 0;
            matchEnd = 0;
            codesStart = 0;
            codesEnd = 0;
            codeStart = 0;
            codeEnd = 0;
            hasCodes = false;
            return false;
        }

        literalStart = lowerBound + relative;
        matchStart = ConsumeWhitespaceBackward(haystack, lowerBound, literalStart);
        int literalEnd = literalStart + NoqaLiteral.Length;
        hasCodes = TryConsumeCodesSuffix(
            haystack,
            literalEnd,
            out codesStart,
            out codesEnd,
            out codeStart,
            out codeEnd);
        matchEnd = hasCodes ? codesEnd : literalEnd;
        return true;
    }

    private bool TryConsumeCodesSuffix(
        ReadOnlySpan<byte> haystack,
        int literalEnd,
        out int codesStart,
        out int codesEnd,
        out int codeStart,
        out int codeEnd)
    {
        codesStart = 0;
        codesEnd = 0;
        codeStart = 0;
        codeEnd = 0;
        if (literalEnd >= haystack.Length ||
            haystack[literalEnd] != (byte)':')
        {
            return false;
        }

        int position = literalEnd + 1;
        if (TryWhitespaceMatchLength(haystack, position, out int whitespaceLength))
        {
            position += whitespaceLength;
        }

        codesStart = position;
        int chunkCount = 0;
        while (TryConsumeCodeChunk(
            haystack,
            position,
            out int next,
            out int chunkStart,
            out int chunkEnd))
        {
            chunkCount++;
            codeStart = chunkStart;
            codeEnd = chunkEnd;
            position = next;
        }

        if (chunkCount == 0)
        {
            codesStart = 0;
            codeStart = 0;
            codeEnd = 0;
            return false;
        }

        codesEnd = position;
        return true;
    }

    private bool TryConsumeCodeChunk(
        ReadOnlySpan<byte> haystack,
        int start,
        out int next,
        out int chunkStart,
        out int chunkEnd)
    {
        next = start;
        chunkStart = start;
        chunkEnd = start;
        int position = start;
        int lettersStart = position;
        while (position < haystack.Length && IsAsciiUpper(haystack[position]))
        {
            position++;
        }

        if (position == lettersStart)
        {
            return false;
        }

        int digitsStart = position;
        while (position < haystack.Length && IsAsciiDigit(haystack[position]))
        {
            position++;
        }

        if (position == digitsStart)
        {
            return false;
        }

        position = ConsumeCodeSeparators(haystack, position);
        chunkEnd = position;
        next = position;
        return true;
    }

    private int ConsumeCodeSeparators(ReadOnlySpan<byte> haystack, int position)
    {
        int current = position;
        while (current < haystack.Length)
        {
            if (haystack[current] == (byte)',')
            {
                current++;
                continue;
            }

            if (!TryWhitespaceMatchLength(haystack, current, out int length))
            {
                break;
            }

            current += length;
        }

        return current;
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

    private static bool TryGetNoqaCapture(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out int noqaCaptureIndex,
        out int codesCaptureIndex,
        out int codeCaptureIndex)
    {
        noqaCaptureIndex = 0;
        codesCaptureIndex = 0;
        codeCaptureIndex = 0;
        if (node is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
            } group)
        {
            return false;
        }

        RegexCompileOptions groupOptions = options.Apply(group.EnabledFlags, group.DisabledFlags);
        RegexSyntaxNode child = UnwrapTransparentNonCapturingGroups(group.Child);
        if (child is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            !TryGetNoqaLiteral(sequence.Nodes[0], groupOptions) ||
            !TryGetNoqaSuffix(sequence.Nodes[1], out codesCaptureIndex, out codeCaptureIndex))
        {
            return false;
        }

        noqaCaptureIndex = group.CaptureIndex;
        return true;
    }

    private static bool TryGetNoqaLiteral(RegexSyntaxNode node, RegexCompileOptions options)
    {
        var bytes = new List<byte>();
        bool sawCaseSensitiveLetter = false;
        if (!TryCollectLiteralBytes(node, options, bytes, ref sawCaseSensitiveLetter))
        {
            return false;
        }

        return !sawCaseSensitiveLetter &&
            bytes.Count == NoqaLiteral.Length &&
            bytes.ToArray().AsSpan().SequenceEqual(NoqaLiteral);
    }

    private static bool TryCollectLiteralBytes(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte> bytes,
        ref bool sawCaseSensitiveLetter)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        switch (node)
        {
            case RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom:
                AppendLiteral(atom.Value.Span, options, bytes, ref sawCaseSensitiveLetter);
                return true;
            case RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom:
                if (!TryGetAsciiCasePair(atom.Value.Span, out byte folded))
                {
                    return false;
                }

                bytes.Add(folded);
                return true;
            case RegexSequenceNode sequence:
                RegexCompileOptions currentOptions = options;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    RegexSyntaxNode child = sequence.Nodes[index];
                    if (child is RegexInlineFlagsNode flags)
                    {
                        currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                        continue;
                    }

                    if (!TryCollectLiteralBytes(child, currentOptions, bytes, ref sawCaseSensitiveLetter))
                    {
                        return false;
                    }
                }

                return true;
            case RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group:
                return TryCollectLiteralBytes(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    bytes,
                    ref sawCaseSensitiveLetter);
            default:
                return false;
        }
    }

    private static void AppendLiteral(
        ReadOnlySpan<byte> literal,
        RegexCompileOptions options,
        List<byte> bytes,
        ref bool sawCaseSensitiveLetter)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            byte value = literal[index];
            if (RegexAsciiCaseInsensitiveFinder.IsAsciiCased(value))
            {
                sawCaseSensitiveLetter |= !options.CaseInsensitive;
                value = RegexAsciiCaseInsensitiveFinder.FoldAscii(value);
            }

            bytes.Add(value);
        }
    }

    private static bool TryGetNoqaSuffix(
        RegexSyntaxNode node,
        out int codesCaptureIndex,
        out int codeCaptureIndex)
    {
        codesCaptureIndex = 0;
        codeCaptureIndex = 0;
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: 1,
                Lazy: false,
            } optional ||
            UnwrapTransparentNonCapturingGroups(optional.Child) is not RegexSequenceNode { Nodes.Count: 3 } sequence ||
            !TryGetLiteral(sequence.Nodes[0], ":"u8) ||
            !TryGetOptionalWhitespace(sequence.Nodes[1]) ||
            !TryGetCodesCapture(sequence.Nodes[2], out codesCaptureIndex, out codeCaptureIndex))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetCodesCapture(
        RegexSyntaxNode node,
        out int codesCaptureIndex,
        out int codeCaptureIndex)
    {
        codesCaptureIndex = 0;
        codeCaptureIndex = 0;
        if (node is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } codesGroup ||
            UnwrapTransparentNonCapturingGroups(codesGroup.Child) is not RegexRepetitionNode
            {
                Minimum: 1,
                Maximum: null,
                Lazy: false,
            } repeated ||
            !TryGetCodeChunkCapture(repeated.Child, out codeCaptureIndex))
        {
            return false;
        }

        codesCaptureIndex = codesGroup.CaptureIndex;
        return true;
    }

    private static bool TryGetCodeChunkCapture(RegexSyntaxNode node, out int codeCaptureIndex)
    {
        codeCaptureIndex = 0;
        if (node is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } codeGroup ||
            UnwrapTransparentNonCapturingGroups(codeGroup.Child) is not RegexSequenceNode { Nodes.Count: 3 } sequence ||
            !TryGetRepeatedClass(sequence.Nodes[0], "A-Z"u8, minimum: 1) ||
            !TryGetRepeatedClass(sequence.Nodes[1], "0-9"u8, minimum: 1) ||
            !TryGetOptionalSeparator(sequence.Nodes[2]))
        {
            return false;
        }

        codeCaptureIndex = codeGroup.CaptureIndex;
        return true;
    }

    private static bool TryGetOptionalWhitespace(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexRepetitionNode
        {
            Minimum: 0,
            Maximum: 1,
            Lazy: false,
        } repetition &&
            UnwrapTransparentNonCapturingGroups(repetition.Child) is RegexAtomNode { Kind: RegexSyntaxKind.WhitespaceClass };
    }

    private static bool TryGetOptionalSeparator(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: 1,
                Lazy: false,
            } optional)
        {
            return false;
        }

        RegexSyntaxNode child = UnwrapTransparentNonCapturingGroups(optional.Child);
        return TryGetRepeatedClass(child, ",\\s"u8, minimum: 1);
    }

    private static bool TryGetRepeatedClass(RegexSyntaxNode node, ReadOnlySpan<byte> expression, int minimum)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexRepetitionNode
        {
            Minimum: var actualMinimum,
            Maximum: null,
            Lazy: false,
        } repetition &&
            actualMinimum == minimum &&
            UnwrapTransparentNonCapturingGroups(repetition.Child) is RegexAtomNode
            {
                Kind: RegexSyntaxKind.CharacterClass,
            } atom &&
            atom.Value.Span.SequenceEqual(expression);
    }

    private static bool TryGetLiteral(RegexSyntaxNode node, ReadOnlySpan<byte> literal)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Span.SequenceEqual(literal);
    }

    private static bool TryGetAsciiCasePair(ReadOnlySpan<byte> expression, out byte folded)
    {
        folded = 0;
        if (expression.Length != 2 ||
            !RegexAsciiCaseInsensitiveFinder.IsAsciiCased(expression[0]) ||
            !RegexAsciiCaseInsensitiveFinder.IsAsciiCased(expression[1]))
        {
            return false;
        }

        byte first = RegexAsciiCaseInsensitiveFinder.FoldAscii(expression[0]);
        byte second = RegexAsciiCaseInsensitiveFinder.FoldAscii(expression[1]);
        if (first != second ||
            expression[0] == expression[1])
        {
            return false;
        }

        folded = first;
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
    private static bool IsAsciiUpper(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiDigit(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }
}
