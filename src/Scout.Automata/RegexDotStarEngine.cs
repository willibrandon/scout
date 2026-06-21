namespace Scout;

internal sealed class RegexDotStarEngine
{
    private readonly bool dotMatchesNewline;
    private readonly bool crlf;
    private readonly bool utf8;
    private readonly bool scalarMode;
    private readonly byte lineTerminator;
    private readonly int minimum;

    private RegexDotStarEngine(RegexCompileOptions options, int minimum)
    {
        dotMatchesNewline = options.DotMatchesNewline;
        crlf = options.Crlf;
        utf8 = options.Utf8;
        scalarMode = options.Utf8 || options.UnicodeClasses;
        lineTerminator = options.LineTerminator;
        this.minimum = minimum;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexDotStarEngine? engine)
    {
        engine = null;
        if (!TryGetDotRunOptions(root, options, out RegexCompileOptions dotOptions, out int minimum))
        {
            return false;
        }

        engine = new RegexDotStarEngine(dotOptions, minimum);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (minimum == 0)
        {
            if (utf8)
            {
                start = FirstUtf8BoundaryAtOrAfter(haystack, start);
            }

            return MatchAt(haystack, start);
        }

        int position = start;
        while (position < haystack.Length)
        {
            if (utf8 && !RegexByteClass.IsUtf8Boundary(haystack, position))
            {
                position++;
                continue;
            }

            RegexMatch? match = MatchAt(haystack, position);
            if (match.HasValue)
            {
                return match;
            }

            position++;
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
        {
            return null;
        }

        int end = FindMatchEnd(haystack, start);
        if (minimum > 0 && end == start)
        {
            return null;
        }

        return new RegexMatch(start, end - start);
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
        int suppressedEmptyStart = -1;
        while (offset <= haystack.Length)
        {
            RegexMatch? match = Find(haystack, offset);
            if (!match.HasValue)
            {
                return total;
            }

            if (match.Value.Length == 0 && match.Value.Start == suppressedEmptyStart)
            {
                offset = Math.Min(match.Value.Start + 1, haystack.Length + 1);
                suppressedEmptyStart = -1;
                continue;
            }

            total += sumSpans ? match.Value.Length : 1;
            if (match.Value.Length == 0)
            {
                suppressedEmptyStart = -1;
                offset = Math.Min(match.Value.End + 1, haystack.Length + 1);
            }
            else
            {
                suppressedEmptyStart = Math.Min(match.Value.End, haystack.Length + 1);
                offset = suppressedEmptyStart;
            }
        }

        return total;
    }

    private int FindMatchEnd(ReadOnlySpan<byte> haystack, int start)
    {
        if (scalarMode)
        {
            return FindScalarMatchEnd(haystack, start);
        }

        if (dotMatchesNewline)
        {
            return haystack.Length;
        }

        if (crlf)
        {
            int offset = haystack[start..].IndexOfAny((byte)'\r', (byte)'\n');
            return offset < 0 ? haystack.Length : start + offset;
        }

        int lineOffset = haystack[start..].IndexOf(lineTerminator);
        return lineOffset < 0 ? haystack.Length : start + lineOffset;
    }

    private int FindScalarMatchEnd(ReadOnlySpan<byte> haystack, int start)
    {
        int position = start;
        while (position < haystack.Length &&
            TryGetScalarDotMatchLength(haystack, position, out int scalarLength))
        {
            position += scalarLength;
        }

        return position;
    }

    private bool TryGetScalarDotMatchLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        if (!RegexByteClass.TryGetUtf8ScalarLength(haystack, position, out int scalarLength) ||
            !DotMatchesScalar(haystack[position]))
        {
            return false;
        }

        length = scalarLength;
        return true;
    }

    private bool DotMatchesScalar(byte firstByte)
    {
        return dotMatchesNewline ||
            (crlf
                ? firstByte is not ((byte)'\n' or (byte)'\r')
                : firstByte != lineTerminator);
    }

    private static int FirstUtf8BoundaryAtOrAfter(ReadOnlySpan<byte> haystack, int start)
    {
        int position = start;
        while (position < haystack.Length && !RegexByteClass.IsUtf8Boundary(haystack, position))
        {
            position++;
        }

        return position;
    }

    private static bool TryGetDotRunOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexCompileOptions dotOptions,
        out int minimum)
    {
        switch (node)
        {
            case RegexInlineFlagsNode flags:
                dotOptions = options.Apply(flags.EnabledFlags, flags.DisabledFlags);
                minimum = 0;
                return false;

            case RegexGroupNode group:
                return TryGetDotRunOptions(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out dotOptions,
                    out minimum);

            case RegexSequenceNode sequence:
                return TryGetSequenceDotRunOptions(sequence, options, out dotOptions, out minimum);

            case RegexRepetitionNode repetition when IsGreedyUnboundedDotRun(repetition, out minimum):
                return TryGetDotAtomOptions(repetition.Child, options, out dotOptions);

            default:
                dotOptions = default;
                minimum = 0;
                return false;
        }
    }

    private static bool TryGetSequenceDotRunOptions(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out RegexCompileOptions dotOptions,
        out int minimum)
    {
        RegexCompileOptions currentOptions = options;
        bool sawDotStar = false;
        dotOptions = default;
        minimum = 0;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode node = sequence.Nodes[index];
            if (node is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!sawDotStar &&
                node is RegexRepetitionNode repetition &&
                IsGreedyUnboundedDotRun(repetition, out minimum) &&
                TryGetDotAtomOptions(repetition.Child, currentOptions, out dotOptions))
            {
                sawDotStar = true;
                continue;
            }

            return false;
        }

        return sawDotStar;
    }

    private static bool TryGetDotAtomOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexCompileOptions dotOptions)
    {
        switch (node)
        {
            case RegexAtomNode { Kind: RegexSyntaxKind.Dot }:
                dotOptions = options;
                return true;

            case RegexGroupNode group:
                return TryGetDotAtomOptions(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out dotOptions);

            case RegexSequenceNode sequence:
                RegexCompileOptions currentOptions = options;
                bool sawDot = false;
                dotOptions = default;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    RegexSyntaxNode child = sequence.Nodes[index];
                    if (child is RegexInlineFlagsNode flags)
                    {
                        currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                        continue;
                    }

                    if (!sawDot && child is RegexAtomNode { Kind: RegexSyntaxKind.Dot })
                    {
                        dotOptions = currentOptions;
                        sawDot = true;
                        continue;
                    }

                    return false;
                }

                return sawDot;

            default:
                dotOptions = default;
                return false;
        }
    }

    private static bool IsGreedyUnboundedDotRun(RegexRepetitionNode repetition, out int minimum)
    {
        minimum = repetition.Minimum;
        return repetition is { Maximum: null, Lazy: false } &&
            repetition.Minimum is 0 or 1;
    }
}
