namespace Scout;

internal sealed class RegexLiteralWordCaptureEngine
{
    private const int MaxBranchCount = 8;

    private readonly RegexLiteralWordCaptureBranch[] branches;
    private readonly byte[][] prefixes;
    private readonly RegexCaseSensitiveLiteralSetScanner? scanner;
    private readonly AhoCorasickAutomaton? automaton;
    private readonly byte[]? commonPrefix;
    private readonly MemmemFinder? commonPrefixFinder;
    private readonly MemmemFinder? singlePrefixFinder;
    private readonly int captureCount;

    private RegexLiteralWordCaptureEngine(
        RegexLiteralWordCaptureBranch[] branches,
        byte[][] prefixes,
        RegexCaseSensitiveLiteralSetScanner? scanner,
        AhoCorasickAutomaton? automaton,
        byte[]? commonPrefix,
        int captureCount)
    {
        this.branches = branches;
        this.prefixes = prefixes;
        this.scanner = scanner;
        this.automaton = automaton;
        this.commonPrefix = commonPrefix;
        this.captureCount = captureCount;
        if (commonPrefix is not null)
        {
            commonPrefixFinder = new MemmemFinder(commonPrefix);
        }

        if (prefixes.Length == 1)
        {
            singlePrefixFinder = new MemmemFinder(prefixes[0]);
        }
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexLiteralWordCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 ||
            options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        List<RegexLiteralWordCaptureBranch> branches = [];
        if (root is RegexAlternationNode alternation)
        {
            if (alternation.Alternatives.Count == 0 ||
                alternation.Alternatives.Count > MaxBranchCount)
            {
                return false;
            }

            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryGetBranch(alternation.Alternatives[index], out RegexLiteralWordCaptureBranch branch))
                {
                    return false;
                }

                branches.Add(branch);
            }
        }
        else if (!TryGetBranch(root, out RegexLiteralWordCaptureBranch branch))
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

        byte[][] prefixes = GetPrefixes(branches);
        RegexCaseSensitiveLiteralSetScanner? scanner = null;
        AhoCorasickAutomaton? automaton = null;
        byte[]? commonPrefix = TryGetCommonPrefix(prefixes);
        if (prefixes.Length > 1 &&
            commonPrefix is null &&
            !RegexCaseSensitiveLiteralSetScanner.TryCreate(prefixes, out scanner))
        {
            automaton = AhoCorasickAutomaton.Create(prefixes, AhoCorasickMatchKind.LeftmostFirst);
        }

        engine = new RegexLiteralWordCaptureEngine(
            branches.ToArray(),
            prefixes,
            scanner,
            automaton,
            commonPrefix,
            captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindPrefix(haystack, searchAt, out RegexLiteralSetCandidate candidate))
        {
            RegexLiteralWordCaptureBranch branch = branches[candidate.LiteralId];
            int captureStart = candidate.Match.End;
            int captureEnd = ConsumeAsciiWord(haystack, captureStart);
            if (captureEnd > captureStart)
            {
                var match = new RegexMatch(candidate.Match.Start, captureEnd - candidate.Match.Start);
                var groups = new RegexMatch?[captureCount + 1];
                groups[0] = match;
                groups[branch.CaptureIndex] = new RegexMatch(captureStart, captureEnd - captureStart);
                return new RegexCaptures(match, groups);
            }

            searchAt = candidate.Match.Start + 1;
        }

        return null;
    }

    private bool TryFindPrefix(ReadOnlySpan<byte> haystack, int startAt, out RegexLiteralSetCandidate candidate)
    {
        if (commonPrefixFinder is not null)
        {
            return TryFindCommonPrefix(haystack, startAt, out candidate);
        }

        if (scanner is not null)
        {
            RegexLiteralSetCandidate? found = scanner.Find(haystack, startAt);
            candidate = found.GetValueOrDefault();
            return found.HasValue;
        }

        if (automaton is not null)
        {
            int automatonStart = Math.Clamp(startAt, 0, haystack.Length);
            AhoCorasickMatch? found = automaton.Find(haystack[automatonStart..]);
            if (!found.HasValue)
            {
                candidate = default;
                return false;
            }

            AhoCorasickMatch match = found.Value;
            candidate = new RegexLiteralSetCandidate(
                match.PatternId,
                new RegexMatch(automatonStart + match.Start, match.Length));
            return true;
        }

        int start = Math.Clamp(startAt, 0, haystack.Length);
        int offset = singlePrefixFinder!.Find(haystack[start..]);
        if (offset < 0)
        {
            candidate = default;
            return false;
        }

        candidate = new RegexLiteralSetCandidate(0, new RegexMatch(start + offset, prefixes[0].Length));
        return true;
    }

    private bool TryFindCommonPrefix(ReadOnlySpan<byte> haystack, int startAt, out RegexLiteralSetCandidate candidate)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt <= haystack.Length - commonPrefix!.Length)
        {
            int offset = commonPrefixFinder!.Find(haystack[searchAt..]);
            if (offset < 0)
            {
                candidate = default;
                return false;
            }

            int start = searchAt + offset;
            for (int index = 0; index < branches.Length; index++)
            {
                ReadOnlySpan<byte> prefix = branches[index].Prefix;
                if (prefix.Length <= haystack.Length - start &&
                    haystack.Slice(start, prefix.Length).SequenceEqual(prefix))
                {
                    candidate = new RegexLiteralSetCandidate(index, new RegexMatch(start, prefix.Length));
                    return true;
                }
            }

            searchAt = start + 1;
        }

        candidate = default;
        return false;
    }

    private static int ConsumeAsciiWord(ReadOnlySpan<byte> haystack, int position)
    {
        while (position < haystack.Length &&
            RegexSimpleSequenceSegment.IsAsciiWord(haystack[position]))
        {
            position++;
        }

        return position;
    }

    private static bool TryGetBranch(RegexSyntaxNode node, out RegexLiteralWordCaptureBranch branch)
    {
        branch = default;
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexSequenceNode sequence ||
            sequence.Nodes.Count < 2)
        {
            return false;
        }

        List<byte> prefix = [];
        for (int index = 0; index < sequence.Nodes.Count - 1; index++)
        {
            if (!TryAppendLiteral(sequence.Nodes[index], prefix))
            {
                return false;
            }
        }

        if (prefix.Count == 0 ||
            !TryGetWordCapture(sequence.Nodes[^1], out int captureIndex))
        {
            return false;
        }

        branch = new RegexLiteralWordCaptureBranch(prefix.ToArray(), captureIndex);
        return true;
    }

    private static bool TryAppendLiteral(RegexSyntaxNode node, List<byte> prefix)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
        {
            return false;
        }

        prefix.AddRange(atom.Value.Span.ToArray());
        return true;
    }

    private static bool TryGetWordCapture(RegexSyntaxNode node, out int captureIndex)
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
                Minimum: 1,
                Maximum: null,
                Lazy: false,
            } repetition ||
            UnwrapTransparentNonCapturingGroups(repetition.Child) is not RegexAtomNode { Kind: RegexSyntaxKind.WordClass })
        {
            return false;
        }

        captureIndex = group.CaptureIndex;
        return true;
    }

    private static byte[][] GetPrefixes(List<RegexLiteralWordCaptureBranch> branches)
    {
        byte[][] prefixes = new byte[branches.Count][];
        for (int index = 0; index < branches.Count; index++)
        {
            prefixes[index] = branches[index].Prefix;
        }

        return prefixes;
    }

    private static byte[]? TryGetCommonPrefix(byte[][] prefixes)
    {
        if (prefixes.Length <= 1)
        {
            return null;
        }

        int length = prefixes[0].Length;
        for (int index = 1; index < prefixes.Length; index++)
        {
            length = Math.Min(length, prefixes[index].Length);
        }

        int commonLength = 0;
        while (commonLength < length)
        {
            byte value = prefixes[0][commonLength];
            for (int index = 1; index < prefixes.Length; index++)
            {
                if (prefixes[index][commonLength] != value)
                {
                    return commonLength >= 2 ? prefixes[0].AsSpan(0, commonLength).ToArray() : null;
                }
            }

            commonLength++;
        }

        return commonLength >= 2 ? prefixes[0].AsSpan(0, commonLength).ToArray() : null;
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
}
