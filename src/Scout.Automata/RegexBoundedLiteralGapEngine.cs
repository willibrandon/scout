namespace Scout;

internal sealed class RegexBoundedLiteralGapEngine
{
    private const int MaxPrefixCount = 8;

    private readonly RegexBoundedLiteralGapBranch[] branches;
    private readonly RegexCaseSensitiveLiteralSetScanner? prefixScanner;
    private readonly MemmemFinder? singlePrefixFinder;
    private readonly byte lineTerminator;

    private RegexBoundedLiteralGapEngine(
        RegexBoundedLiteralGapBranch[] branches,
        RegexCaseSensitiveLiteralSetScanner? prefixScanner,
        byte lineTerminator)
    {
        this.branches = branches;
        this.prefixScanner = prefixScanner;
        this.lineTerminator = lineTerminator;
        if (branches.Length == 1)
        {
            singlePrefixFinder = new MemmemFinder(branches[0].Prefix);
        }
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexBoundedLiteralGapEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            options.DotMatchesNewline ||
            options.Crlf ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        var branches = new List<RegexBoundedLiteralGapBranch>();
        if (root is RegexAlternationNode alternation)
        {
            if (alternation.Alternatives.Count == 0 || alternation.Alternatives.Count > MaxPrefixCount)
            {
                return false;
            }

            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryGetBranch(alternation.Alternatives[index], out RegexBoundedLiteralGapBranch branch))
                {
                    return false;
                }

                branches.Add(branch);
            }
        }
        else if (!TryGetBranch(root, out RegexBoundedLiteralGapBranch branch))
        {
            return false;
        }
        else
        {
            branches.Add(branch);
        }

        if (branches.Count == 0)
        {
            return false;
        }

        RegexCaseSensitiveLiteralSetScanner? prefixScanner = null;
        if (branches.Count > 1 &&
            !RegexCaseSensitiveLiteralSetScanner.TryCreate(GetPrefixes(branches), out prefixScanner))
        {
            return false;
        }

        engine = new RegexBoundedLiteralGapEngine(branches.ToArray(), prefixScanner, options.LineTerminator);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindPrefix(haystack, searchAt, out RegexLiteralSetCandidate candidate))
        {
            int start = candidate.Match.Start;
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
        if ((uint)start >= (uint)haystack.Length)
        {
            return false;
        }

        for (int index = 0; index < branches.Length; index++)
        {
            RegexBoundedLiteralGapBranch branch = branches[index];
            if (!PrefixMatchesAt(haystack, start, branch.Prefix) ||
                !TryFindGreedySuffix(haystack, start + branch.Prefix.Length, branch, out int suffixStart))
            {
                continue;
            }

            length = suffixStart + branch.Suffix.Length - start;
            return true;
        }

        return false;
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

    private bool TryFindGreedySuffix(
        ReadOnlySpan<byte> haystack,
        int gapStart,
        RegexBoundedLiteralGapBranch branch,
        out int suffixStart)
    {
        suffixStart = 0;
        int minSuffixStart = gapStart + branch.Minimum;
        int maxSuffixStart = Math.Min(gapStart + branch.Maximum, haystack.Length - branch.Suffix.Length);
        if (minSuffixStart > maxSuffixStart)
        {
            return false;
        }

        int scanLength = maxSuffixStart - gapStart + 1;
        int lineTerminatorOffset = haystack.Slice(gapStart, scanLength).IndexOf(lineTerminator);
        if (lineTerminatorOffset >= 0)
        {
            maxSuffixStart = Math.Min(maxSuffixStart, gapStart + lineTerminatorOffset);
            if (minSuffixStart > maxSuffixStart)
            {
                return false;
            }
        }

        ReadOnlySpan<byte> window = haystack.Slice(
            minSuffixStart,
            maxSuffixStart - minSuffixStart + branch.Suffix.Length);
        int relative = window.LastIndexOf(branch.Suffix);
        if (relative < 0)
        {
            return false;
        }

        suffixStart = minSuffixStart + relative;
        return true;
    }

    private bool TryFindPrefix(ReadOnlySpan<byte> haystack, int startAt, out RegexLiteralSetCandidate candidate)
    {
        if (prefixScanner is not null)
        {
            RegexLiteralSetCandidate? found = prefixScanner.Find(haystack, startAt);
            candidate = found.GetValueOrDefault();
            return found.HasValue;
        }

        int start = Math.Clamp(startAt, 0, haystack.Length);
        int offset = singlePrefixFinder!.Find(haystack[start..]);
        if (offset < 0)
        {
            candidate = default;
            return false;
        }

        candidate = new RegexLiteralSetCandidate(0, new RegexMatch(start + offset, branches[0].Prefix.Length));
        return true;
    }

    private static bool PrefixMatchesAt(ReadOnlySpan<byte> haystack, int start, byte[] prefix)
    {
        return prefix.Length <= haystack.Length - start &&
            haystack.Slice(start, prefix.Length).SequenceEqual(prefix);
    }

    private static bool TryGetBranch(RegexSyntaxNode node, out RegexBoundedLiteralGapBranch branch)
    {
        branch = default;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexSequenceNode { Nodes.Count: >= 3 } sequence)
        {
            return false;
        }

        int index = 0;
        var prefix = new List<byte>();
        while (index < sequence.Nodes.Count && TryAppendAsciiLiteral(sequence.Nodes[index], prefix))
        {
            index++;
        }

        if (prefix.Count == 0 ||
            index >= sequence.Nodes.Count ||
            !TryGetBoundedDotRepetition(sequence.Nodes[index], out int minimum, out int maximum))
        {
            return false;
        }

        index++;
        var suffix = new List<byte>();
        while (index < sequence.Nodes.Count && TryAppendAsciiLiteral(sequence.Nodes[index], suffix))
        {
            index++;
        }

        if (suffix.Count == 0 || index != sequence.Nodes.Count)
        {
            return false;
        }

        branch = new RegexBoundedLiteralGapBranch(prefix.ToArray(), suffix.ToArray(), minimum, maximum);
        return true;
    }

    private static bool TryAppendAsciiLiteral(RegexSyntaxNode node, List<byte> literal)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom ||
            atom.Value.Length == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = atom.Value.Span;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] > 0x7F)
            {
                return false;
            }

            literal.Add(bytes[index]);
        }

        return true;
    }

    private static bool TryGetBoundedDotRepetition(RegexSyntaxNode node, out int minimum, out int maximum)
    {
        minimum = 0;
        maximum = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: >= 0,
                Maximum: { } candidateMaximum,
                Lazy: false,
            } repetition ||
            UnwrapTransparentGroups(repetition.Child) is not RegexAtomNode { Kind: RegexSyntaxKind.Dot })
        {
            return false;
        }

        minimum = repetition.Minimum;
        maximum = candidateMaximum;
        return maximum >= minimum;
    }

    private static byte[][] GetPrefixes(List<RegexBoundedLiteralGapBranch> branches)
    {
        byte[][] prefixes = new byte[branches.Count][];
        for (int index = 0; index < branches.Count; index++)
        {
            prefixes[index] = branches[index].Prefix;
        }

        return prefixes;
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
