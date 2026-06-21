using System.Buffers;

namespace Scout;

internal sealed class RegexBoundedLineLiteralGapEngine
{
    private const int MaxPrefixCount = 8;
    private const int MaxRuns = 32;
    private static readonly SearchValues<byte> RegexWhitespaceBytes = SearchValues.Create(" \t\n\r\f\v"u8);

    private readonly RegexBoundedLineLiteralGapBranch[] branches;
    private readonly RegexCaseSensitiveLiteralSetScanner? prefixScanner;
    private readonly MemmemFinder? singlePrefixFinder;
    private readonly bool distinctFirstBytePrefixes;
    private readonly byte firstPrefixByte;
    private readonly byte secondPrefixByte;
    private readonly byte lineTerminator;

    private RegexBoundedLineLiteralGapEngine(
        RegexBoundedLineLiteralGapBranch[] branches,
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
        else if (CanUseDistinctFirstByteScanner(branches))
        {
            distinctFirstBytePrefixes = true;
            firstPrefixByte = branches[0].Prefix[0];
            secondPrefixByte = branches[1].Prefix[0];
        }
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexBoundedLineLiteralGapEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            options.DotMatchesNewline ||
            options.Crlf ||
            !IsRegexWhitespaceByte(options.LineTerminator) ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        var branches = new List<RegexBoundedLineLiteralGapBranch>();
        if (root is RegexAlternationNode alternation)
        {
            if (alternation.Alternatives.Count == 0 || alternation.Alternatives.Count > MaxPrefixCount)
            {
                return false;
            }

            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryGetBranch(alternation.Alternatives[index], out RegexBoundedLineLiteralGapBranch branch))
                {
                    return false;
                }

                branches.Add(branch);
            }
        }
        else if (!TryGetBranch(root, out RegexBoundedLineLiteralGapBranch branch))
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
            !CanUseDistinctFirstByteScanner(branches) &&
            !RegexCaseSensitiveLiteralSetScanner.TryCreate(GetPrefixes(branches), out prefixScanner))
        {
            return false;
        }

        engine = new RegexBoundedLineLiteralGapEngine(branches.ToArray(), prefixScanner, options.LineTerminator);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindPrefix(haystack, searchAt, out RegexLiteralSetCandidate candidate))
        {
            int start = candidate.Match.Start;
            if (TryMatchVerifiedCandidateAt(haystack, start, candidate.LiteralId, out int length))
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
            RegexBoundedLineLiteralGapBranch branch = branches[index];
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

    private bool TryMatchCandidateAt(ReadOnlySpan<byte> haystack, int start, int literalId, out int length)
    {
        if ((uint)literalId < (uint)branches.Length &&
            TryMatchBranchAt(haystack, start, branches[literalId], out length))
        {
            return true;
        }

        for (int index = 0; index < branches.Length; index++)
        {
            if (index == literalId)
            {
                continue;
            }

            if (TryMatchBranchAt(haystack, start, branches[index], out length))
            {
                return true;
            }
        }

        length = 0;
        return false;
    }

    private bool TryMatchBranchAt(
        ReadOnlySpan<byte> haystack,
        int start,
        RegexBoundedLineLiteralGapBranch branch,
        out int length)
    {
        if (PrefixMatchesAt(haystack, start, branch.Prefix) &&
            TryFindGreedySuffix(haystack, start + branch.Prefix.Length, branch, out int suffixStart))
        {
            length = suffixStart + branch.Suffix.Length - start;
            return true;
        }

        length = 0;
        return false;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindPrefix(haystack, offset, out RegexLiteralSetCandidate candidate))
        {
            int start = candidate.Match.Start;
            if (TryMatchVerifiedCandidateAt(haystack, start, candidate.LiteralId, out int length))
            {
                total += sumSpans ? length : 1;
                offset = start + length;
                continue;
            }

            offset = start + 1;
        }

        return total;
    }

    private bool TryMatchVerifiedCandidateAt(ReadOnlySpan<byte> haystack, int start, int literalId, out int length)
    {
        if ((uint)literalId < (uint)branches.Length)
        {
            RegexBoundedLineLiteralGapBranch branch = branches[literalId];
            if (TryFindGreedySuffix(haystack, start + branch.Prefix.Length, branch, out int suffixStart))
            {
                length = suffixStart + branch.Suffix.Length - start;
                return true;
            }
        }

        for (int index = 0; index < branches.Length; index++)
        {
            if (index == literalId)
            {
                continue;
            }

            if (TryMatchBranchAt(haystack, start, branches[index], out length))
            {
                return true;
            }
        }

        length = 0;
        return false;
    }

    private bool TryFindGreedySuffix(
        ReadOnlySpan<byte> haystack,
        int gapStart,
        RegexBoundedLineLiteralGapBranch branch,
        out int suffixStart)
    {
        suffixStart = 0;
        int maxStart = haystack.Length - branch.Suffix.Length;
        if (gapStart > maxStart)
        {
            return false;
        }

        int maxSuffixStart = GetMaxSuffixStart(haystack, gapStart, maxStart, branch.MaximumRuns);
        ReadOnlySpan<byte> window = haystack.Slice(gapStart, maxSuffixStart - gapStart + branch.Suffix.Length);
        int relative = window.LastIndexOf(branch.Suffix);
        while (relative >= 0)
        {
            int candidateStart = gapStart + relative;
            if (candidateStart <= maxSuffixStart &&
                GapCanMatchWithinBound(haystack, gapStart, candidateStart))
            {
                suffixStart = candidateStart;
                return true;
            }

            if (relative == 0)
            {
                break;
            }

            window = window[..relative];
            relative = window.LastIndexOf(branch.Suffix);
        }

        return false;
    }

    private int GetMaxSuffixStart(ReadOnlySpan<byte> haystack, int gapStart, int maxStart, int maximumRuns)
    {
        int runs = 0;
        int index = gapStart;
        while (index <= maxStart)
        {
            ReadOnlySpan<byte> remaining = haystack.Slice(index, maxStart - index + 1);
            int lineTerminatorOffset = remaining.IndexOf(lineTerminator);
            int lineLength = lineTerminatorOffset < 0 ? remaining.Length : lineTerminatorOffset;
            int nonWhitespaceOffset = remaining[..lineLength].IndexOfAnyExcept(RegexWhitespaceBytes);
            if (nonWhitespaceOffset >= 0)
            {
                runs++;
                if (runs > maximumRuns)
                {
                    return index + nonWhitespaceOffset;
                }
            }

            if (lineTerminatorOffset < 0)
            {
                return maxStart;
            }

            index += lineTerminatorOffset + 1;
        }

        return maxStart;
    }

    private bool GapCanMatchWithinBound(ReadOnlySpan<byte> haystack, int gapStart, int suffixStart)
    {
        if (suffixStart == gapStart)
        {
            return true;
        }

        return haystack.Slice(gapStart, suffixStart - gapStart).IndexOfAnyExcept(lineTerminator) >= 0;
    }

    private bool TryFindPrefix(ReadOnlySpan<byte> haystack, int startAt, out RegexLiteralSetCandidate candidate)
    {
        if (distinctFirstBytePrefixes)
        {
            return TryFindDistinctFirstBytePrefix(haystack, startAt, out candidate);
        }

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

    private bool TryFindDistinctFirstBytePrefix(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out RegexLiteralSetCandidate candidate)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        while (start < haystack.Length)
        {
            int offset = haystack[start..].IndexOfAny(firstPrefixByte, secondPrefixByte);
            if (offset < 0)
            {
                candidate = default;
                return false;
            }

            int candidateStart = start + offset;
            int literalId = haystack[candidateStart] == firstPrefixByte ? 0 : 1;
            byte[] prefix = branches[literalId].Prefix;
            if (PrefixMatchesAt(haystack, candidateStart, prefix))
            {
                candidate = new RegexLiteralSetCandidate(literalId, new RegexMatch(candidateStart, prefix.Length));
                return true;
            }

            start = candidateStart + 1;
        }

        candidate = default;
        return false;
    }

    private static bool PrefixMatchesAt(ReadOnlySpan<byte> haystack, int start, byte[] prefix)
    {
        return prefix.Length <= haystack.Length - start &&
            haystack.Slice(start, prefix.Length).SequenceEqual(prefix);
    }

    private static bool CanUseDistinctFirstByteScanner(IReadOnlyList<RegexBoundedLineLiteralGapBranch> branches)
    {
        return branches.Count == 2 &&
            branches[0].Prefix.Length > 0 &&
            branches[1].Prefix.Length > 0 &&
            branches[0].Prefix[0] != branches[1].Prefix[0];
    }

    private static bool TryGetBranch(RegexSyntaxNode node, out RegexBoundedLineLiteralGapBranch branch)
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
            !TryGetBoundedWhitespaceDotRunRepetition(sequence.Nodes[index], out int maximumRuns))
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

        branch = new RegexBoundedLineLiteralGapBranch(prefix.ToArray(), suffix.ToArray(), maximumRuns);
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

    private static bool TryGetBoundedWhitespaceDotRunRepetition(RegexSyntaxNode node, out int maximumRuns)
    {
        maximumRuns = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: { } maximum,
                Lazy: false,
            } repetition ||
            maximum <= 0 ||
            maximum > MaxRuns ||
            !TryGetWhitespaceDotRun(UnwrapTransparentGroups(repetition.Child)))
        {
            return false;
        }

        maximumRuns = maximum;
        return true;
    }

    private static bool TryGetWhitespaceDotRun(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexSequenceNode { Nodes.Count: 3 } sequence)
        {
            return false;
        }

        return IsGreedyRepeatedAtom(sequence.Nodes[0], RegexSyntaxKind.WhitespaceClass, minimum: 0) &&
            IsGreedyRepeatedAtom(sequence.Nodes[1], RegexSyntaxKind.Dot, minimum: 1) &&
            IsGreedyRepeatedAtom(sequence.Nodes[2], RegexSyntaxKind.WhitespaceClass, minimum: 0);
    }

    private static bool IsGreedyRepeatedAtom(RegexSyntaxNode node, RegexSyntaxKind atomKind, int minimum)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode
        {
            Minimum: var actualMinimum,
            Maximum: null,
            Lazy: false,
        } repetition &&
            actualMinimum == minimum &&
            UnwrapTransparentGroups(repetition.Child) is RegexAtomNode { Kind: var actualKind } &&
            actualKind == atomKind;
    }

    private static bool IsRegexWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }

    private static byte[][] GetPrefixes(List<RegexBoundedLineLiteralGapBranch> branches)
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
