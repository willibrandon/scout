namespace Scout;

internal sealed class RegexDotStarEngine
{
    private readonly bool dotMatchesNewline;
    private readonly bool crlf;
    private readonly byte lineTerminator;

    private RegexDotStarEngine(RegexCompileOptions options)
    {
        dotMatchesNewline = options.DotMatchesNewline;
        crlf = options.Crlf;
        lineTerminator = options.LineTerminator;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexDotStarEngine? engine)
    {
        engine = null;
        if (!TryGetDotStarOptions(root, options, out RegexCompileOptions dotOptions) || dotOptions.Utf8)
        {
            return false;
        }

        engine = new RegexDotStarEngine(dotOptions);
        return true;
    }

    public RegexMatch Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int end = FindMatchEnd(haystack, start);
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
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (dotMatchesNewline)
        {
            return sumSpans ? haystack.Length - start : 1;
        }

        long total = 0;
        int offset = start;
        int suppressedEmptyStart = -1;
        while (offset <= haystack.Length)
        {
            int end = FindMatchEnd(haystack, offset);
            int length = end - offset;
            if (length != 0 || offset != suppressedEmptyStart)
            {
                total += sumSpans ? length : 1;
            }

            if (length == 0)
            {
                suppressedEmptyStart = -1;
                offset = Math.Min(offset + 1, haystack.Length + 1);
            }
            else
            {
                suppressedEmptyStart = end;
                offset = end;
            }
        }

        return total;
    }

    private int FindMatchEnd(ReadOnlySpan<byte> haystack, int start)
    {
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

    private static bool TryGetDotStarOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexCompileOptions dotOptions)
    {
        switch (node)
        {
            case RegexInlineFlagsNode flags:
                dotOptions = options.Apply(flags.EnabledFlags, flags.DisabledFlags);
                return false;

            case RegexGroupNode group:
                return TryGetDotStarOptions(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out dotOptions);

            case RegexSequenceNode sequence:
                return TryGetSequenceDotStarOptions(sequence, options, out dotOptions);

            case RegexRepetitionNode repetition when IsGreedyStar(repetition):
                return TryGetDotAtomOptions(repetition.Child, options, out dotOptions);

            default:
                dotOptions = default;
                return false;
        }
    }

    private static bool TryGetSequenceDotStarOptions(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out RegexCompileOptions dotOptions)
    {
        RegexCompileOptions currentOptions = options;
        bool sawDotStar = false;
        dotOptions = default;
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
                IsGreedyStar(repetition) &&
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

    private static bool IsGreedyStar(RegexRepetitionNode repetition)
    {
        return repetition is { Minimum: 0, Maximum: null, Lazy: false };
    }
}
