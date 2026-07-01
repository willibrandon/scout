namespace Scout;

internal sealed class RegexBoundedDigitDelimiterEngine
{
    private const int MaximumGroups = 4;
    private const int MaximumDigitRunLength = 8;

    private readonly RegexBoundedDigitDelimiterRun[] runs;
    private readonly byte[] delimiters;

    private RegexBoundedDigitDelimiterEngine(RegexBoundedDigitDelimiterRun[] runs, byte[] delimiters)
    {
        this.runs = runs;
        this.delimiters = delimiters;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexBoundedDigitDelimiterEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            UnwrapTransparentGroups(root) is not RegexSequenceNode sequence ||
            !TryReadDigitDelimiterSequence(sequence, options, out RegexBoundedDigitDelimiterRun[]? runs, out byte[]? delimiters))
        {
            return false;
        }

        engine = new RegexBoundedDigitDelimiterEngine(runs, delimiters);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        while (start < haystack.Length)
        {
            int digitOffset = IndexOfAsciiDigit(haystack[start..]);
            if (digitOffset < 0)
            {
                return null;
            }

            start += digitOffset;
            if (TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }

            start++;
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

    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        return Find(haystack, startAt: 0).HasValue;
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
        while (Find(haystack, offset) is RegexMatch match)
        {
            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return total;
    }

    private bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if (TryMatchRun(haystack, groupIndex: 0, start, out int end))
        {
            length = end - start;
            return true;
        }

        return false;
    }

    private bool TryMatchRun(ReadOnlySpan<byte> haystack, int groupIndex, int offset, out int end)
    {
        end = 0;
        RegexBoundedDigitDelimiterRun run = runs[groupIndex];
        int available = 0;
        while (available < run.Maximum &&
            offset + available < haystack.Length &&
            IsAsciiDigit(haystack[offset + available]))
        {
            available++;
        }

        int maximum = Math.Min(available, run.Maximum);
        for (int length = maximum; length >= run.Minimum; length--)
        {
            if (!run.Allows(length))
            {
                continue;
            }

            int next = offset + length;
            if (groupIndex == delimiters.Length)
            {
                end = next;
                return true;
            }

            if (next < haystack.Length &&
                haystack[next] == delimiters[groupIndex] &&
                TryMatchRun(haystack, groupIndex + 1, next + 1, out end))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDigitDelimiterSequence(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out RegexBoundedDigitDelimiterRun[] runs,
        out byte[] delimiters)
    {
        runs = [];
        delimiters = [];
        if (sequence.Nodes.Count < 3 || sequence.Nodes.Count % 2 == 0)
        {
            return false;
        }

        int groupCount = (sequence.Nodes.Count + 1) / 2;
        if (groupCount > MaximumGroups)
        {
            return false;
        }

        var parsedRuns = new RegexBoundedDigitDelimiterRun[groupCount];
        byte[] parsedDelimiters = new byte[groupCount - 1];
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            if ((index & 1) == 0)
            {
                if (!TryReadDigitRun(sequence.Nodes[index], options, out parsedRuns[index / 2]))
                {
                    return false;
                }
            }
            else if (!TryReadDelimiter(sequence.Nodes[index], out parsedDelimiters[index / 2]))
            {
                return false;
            }
        }

        runs = parsedRuns;
        delimiters = parsedDelimiters;
        return true;
    }

    private static bool TryReadDigitRun(RegexSyntaxNode node, RegexCompileOptions options, out RegexBoundedDigitDelimiterRun run)
    {
        node = UnwrapGroups(node, ref options);
        if (TryReadSingleDigitPiece(node, options, out run))
        {
            return true;
        }

        if (node is not RegexSequenceNode sequence || sequence.Nodes.Count == 0)
        {
            run = default;
            return false;
        }

        RegexBoundedDigitDelimiterRun accumulated = RegexBoundedDigitDelimiterRun.Empty;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryReadSingleDigitPiece(child, currentOptions, out RegexBoundedDigitDelimiterRun piece))
            {
                run = default;
                return false;
            }

            accumulated = RegexBoundedDigitDelimiterRun.Concatenate(accumulated, piece);
            if (accumulated.Maximum > MaximumDigitRunLength)
            {
                run = default;
                return false;
            }
        }

        if (accumulated.Minimum <= 0)
        {
            run = default;
            return false;
        }

        run = accumulated;
        return true;
    }

    private static bool TryReadSingleDigitPiece(RegexSyntaxNode node, RegexCompileOptions options, out RegexBoundedDigitDelimiterRun run)
    {
        node = UnwrapGroups(node, ref options);
        if (IsDigitAtom(node, options))
        {
            run = RegexBoundedDigitDelimiterRun.Range(1, 1);
            return true;
        }

        if (node is RegexRepetitionNode repetition &&
            !repetition.Lazy &&
            repetition.Minimum >= 0 &&
            repetition.Maximum is int maximum &&
            maximum >= repetition.Minimum &&
            maximum <= MaximumDigitRunLength)
        {
            if (IsDigitAtom(repetition.Child, options))
            {
                run = RegexBoundedDigitDelimiterRun.Range(repetition.Minimum, maximum);
                return true;
            }

            RegexCompileOptions childOptions = options;
            if (TryReadDigitRun(repetition.Child, childOptions, out RegexBoundedDigitDelimiterRun childRun) &&
                childRun.Maximum * maximum <= MaximumDigitRunLength)
            {
                run = RegexBoundedDigitDelimiterRun.Repeat(childRun, repetition.Minimum, maximum);
                return true;
            }
        }

        run = default;
        return false;
    }

    private static bool IsDigitAtom(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapGroups(node, ref options);
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

        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bool actual = RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                atom.Value.Span,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator);
            if (actual != IsAsciiDigit((byte)value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadDelimiter(RegexSyntaxNode node, out byte delimiter)
    {
        delimiter = 0;
        node = UnwrapTransparentGroups(node);
        if (node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Length == 1)
        {
            delimiter = atom.Value.Span[0];
            return !IsAsciiDigit(delimiter);
        }

        return false;
    }

    private static RegexSyntaxNode UnwrapGroups(RegexSyntaxNode node, ref RegexCompileOptions options)
    {
        while (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.NonCapturingGroup or RegexSyntaxKind.CapturingGroup,
            } group)
        {
            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            node = group.Child;
        }

        return node;
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
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

    private static int IndexOfAsciiDigit(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            if (IsAsciiDigit(bytes[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsAsciiDigit(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }
}
