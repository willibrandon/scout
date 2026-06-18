namespace Scout;

internal sealed class RegexLeadingClassLiteralEngine
{
    private const int MaxLiteralCount = 8;
    private const int MaxExpandedSearchPatternCount = 16;
    private const int MaxTrailingSearchBytes = 32;
    private const int TrailingSearchWindowLength = 5;

    private readonly RegexLeadingClassLiteralBranch[] branches;
    private readonly byte[][] searchPatterns;
    private readonly int[] searchPatternBranchIds;
    private readonly int[] searchPatternLiteralOffsets;
    private readonly bool searchPatternsIncludeTrailingAtom;
    private readonly bool searchPatternCandidateBranchesAreUnambiguous;
    private readonly RegexCaseSensitiveLiteralSetScanner? scanner;
    private readonly RegexPackedLiteralSetScanner? packedScanner;
    private readonly AhoCorasickAutomaton? automaton;
    private readonly MemmemFinder? singleLiteralFinder;
    private readonly RegexTwoPrefixLiteralScanner? branchLiteralScanner;
    private readonly RegexLeadingClassTrailingAnchorScanner? trailingAnchorScanner;
    private readonly RegexCompileOptions options;

    private RegexLeadingClassLiteralEngine(
        RegexLeadingClassLiteralBranch[] branches,
        byte[][] searchPatterns,
        int[] searchPatternBranchIds,
        int[] searchPatternLiteralOffsets,
        bool searchPatternsIncludeTrailingAtom,
        bool searchPatternCandidateBranchesAreUnambiguous,
        RegexCaseSensitiveLiteralSetScanner? scanner,
        RegexPackedLiteralSetScanner? packedScanner,
        AhoCorasickAutomaton? automaton,
        RegexTwoPrefixLiteralScanner? branchLiteralScanner,
        RegexLeadingClassTrailingAnchorScanner? trailingAnchorScanner,
        RegexCompileOptions options)
    {
        this.branches = branches;
        this.searchPatterns = searchPatterns;
        this.searchPatternBranchIds = searchPatternBranchIds;
        this.searchPatternLiteralOffsets = searchPatternLiteralOffsets;
        this.searchPatternsIncludeTrailingAtom = searchPatternsIncludeTrailingAtom;
        this.searchPatternCandidateBranchesAreUnambiguous = searchPatternCandidateBranchesAreUnambiguous;
        this.scanner = scanner;
        this.packedScanner = packedScanner;
        this.automaton = automaton;
        this.branchLiteralScanner = branchLiteralScanner;
        this.trailingAnchorScanner = trailingAnchorScanner;
        this.options = options;
        if (searchPatterns.Length == 1)
        {
            singleLiteralFinder = new MemmemFinder(searchPatterns[0]);
        }
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexLeadingClassLiteralEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            options.SwapGreed)
        {
            return false;
        }

        if (!TrySplitOuterTrailingAtom(root, options, out RegexSyntaxNode branchRoot, out RegexAtomSpec? trailingAtom))
        {
            branchRoot = root;
            trailingAtom = null;
        }

        branchRoot = UnwrapTransparentGroups(branchRoot);
        var branches = new List<RegexLeadingClassLiteralBranch>();
        if (branchRoot is RegexAlternationNode alternation)
        {
            if (alternation.Alternatives.Count == 0 || alternation.Alternatives.Count > MaxLiteralCount)
            {
                return false;
            }

            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryGetBranch(alternation.Alternatives[index], trailingAtom, out RegexLeadingClassLiteralBranch branch))
                {
                    return false;
                }

                branches.Add(branch);
            }
        }
        else if (!TryGetBranch(branchRoot, trailingAtom, out RegexLeadingClassLiteralBranch branch))
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

        byte[][] literals = GetLiterals(branches);
        int[] literalBranchIds = BuildBranchIds(literals.Length);
        int[] literalOffsets = new int[literals.Length];
        byte[][]? expandedPatterns = TryBuildTrailingSearchPatterns(
            branches,
            options,
            preferTrailingWindow: true,
            out int[]? expandedBranchIds,
            out int[]? expandedLiteralOffsets);
        if (expandedPatterns is not null &&
            expandedLiteralOffsets is not null &&
            HasNonZeroOffset(expandedLiteralOffsets) &&
            !SearchPatternCandidateBranchesAreUnambiguous(expandedPatterns))
        {
            expandedPatterns = TryBuildTrailingSearchPatterns(
                branches,
                options,
                preferTrailingWindow: false,
                out expandedBranchIds,
                out expandedLiteralOffsets);
        }

        byte[][] searchPatterns = expandedPatterns ?? literals;
        int[] searchPatternBranchIds = expandedBranchIds ?? literalBranchIds;
        int[] searchPatternLiteralOffsets = expandedLiteralOffsets ?? literalOffsets;
        bool expandedSearchPatterns = expandedPatterns is not null;
        RegexCaseSensitiveLiteralSetScanner? scanner = null;
        RegexPackedLiteralSetScanner? packedScanner = null;
        AhoCorasickAutomaton? automaton = null;
        if (searchPatterns.Length > 1)
        {
            bool scannerCreated = expandedSearchPatterns &&
                RegexPackedLiteralSetScanner.TryCreateWithMaxLiteralCount(
                    searchPatterns,
                    MaxExpandedSearchPatternCount,
                    out packedScanner);
            scannerCreated = scannerCreated || (expandedSearchPatterns
                ? RegexCaseSensitiveLiteralSetScanner.TryCreateWithMaxLiteralCount(
                    searchPatterns,
                    MaxExpandedSearchPatternCount,
                    out scanner)
                : RegexCaseSensitiveLiteralSetScanner.TryCreate(searchPatterns, out scanner));
            if (!scannerCreated)
            {
                if (expandedSearchPatterns)
                {
                    searchPatterns = literals;
                    searchPatternBranchIds = literalBranchIds;
                    expandedSearchPatterns = false;
                    packedScanner = null;
                    scannerCreated = RegexCaseSensitiveLiteralSetScanner.TryCreate(searchPatterns, out scanner);
                }

                if (!scannerCreated)
                {
                    automaton = AhoCorasickAutomaton.Create(searchPatterns, AhoCorasickMatchKind.LeftmostFirst);
                }
            }
        }

        RegexLeadingClassTrailingAnchorScanner? trailingAnchorScanner = null;
        if (!expandedSearchPatterns)
        {
            RegexLeadingClassTrailingAnchorScanner.TryCreate(branches, options, out trailingAnchorScanner);
        }

        RegexTwoPrefixLiteralScanner? branchLiteralScanner = null;
        if (branches.Count == 2)
        {
            RegexTwoPrefixLiteralScanner.TryCreate(literals, out branchLiteralScanner);
        }

        engine = new RegexLeadingClassLiteralEngine(
            branches.ToArray(),
            searchPatterns,
            searchPatternBranchIds,
            searchPatternLiteralOffsets,
            expandedSearchPatterns,
            SearchPatternCandidateBranchesAreUnambiguous(searchPatterns),
            scanner,
            packedScanner,
            automaton,
            branchLiteralScanner,
            trailingAnchorScanner,
            options);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        if (trailingAnchorScanner is not null)
        {
            return FindByTrailingAnchor(haystack, lowerBound);
        }

        if (branchLiteralScanner is not null)
        {
            return FindByBranchLiteralScanner(haystack, lowerBound);
        }

        int searchAt = Math.Min(haystack.Length, lowerBound + 1);
        while (TryFindLiteral(haystack, searchAt, out RegexLiteralSetCandidate candidate))
        {
            int start = CandidateMatchStart(candidate);
            int length = 0;
            bool matched = start >= lowerBound &&
                (searchPatternCandidateBranchesAreUnambiguous
                    ? TryMatchCandidateAt(haystack, start, candidate, out length)
                    : TryMatchAt(haystack, start, out length));
            if (matched)
            {
                return new RegexMatch(start, length);
            }

            searchAt = candidate.Match.Start + 1;
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
            RegexLeadingClassLiteralBranch branch = branches[index];
            if (!LeadingByteMatches(haystack[start], branch.LeadingKind) ||
                branch.Literal.Length > haystack.Length - start - 1 ||
                !haystack.Slice(start + 1, branch.Literal.Length).SequenceEqual(branch.Literal))
            {
                continue;
            }

            int end = start + 1 + branch.Literal.Length;
            if (branch.TrailingAtom is { } trailingAtom)
            {
                if (end >= haystack.Length || !AtomMatches(haystack[end], trailingAtom, options))
                {
                    continue;
                }

                end++;
            }

            length = end - start;
            return true;
        }

        return false;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (trailingAnchorScanner is not null)
        {
            return CountOrSumByTrailingAnchor(haystack, startOffset, sumSpans);
        }

        if (branchLiteralScanner is not null)
        {
            return CountOrSumByBranchLiteralScanner(haystack, startOffset, sumSpans);
        }

        long total = 0;
        int offset = startOffset;
        int searchAt = Math.Min(haystack.Length, offset + 1);
        while (TryFindLiteral(haystack, searchAt, out RegexLiteralSetCandidate candidate))
        {
            int start = CandidateMatchStart(candidate);
            int length = 0;
            bool matched = start >= offset &&
                (searchPatternCandidateBranchesAreUnambiguous
                    ? TryMatchCandidateAt(haystack, start, candidate, out length)
                    : TryMatchAt(haystack, start, out length));
            if (matched)
            {
                total += sumSpans ? length : 1;
                offset = start + length;
                searchAt = Math.Min(haystack.Length, offset + 1);
            }
            else
            {
                searchAt = candidate.Match.Start + 1;
            }
        }

        return total;
    }

    private bool TryMatchCandidateAt(
        ReadOnlySpan<byte> haystack,
        int start,
        RegexLiteralSetCandidate candidate,
        out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            (uint)candidate.LiteralId >= (uint)searchPatternBranchIds.Length)
        {
            return false;
        }

        RegexLeadingClassLiteralBranch branch = branches[searchPatternBranchIds[candidate.LiteralId]];
        if (!LeadingByteMatches(haystack[start], branch.LeadingKind))
        {
            return false;
        }

        int literalOffset = searchPatternLiteralOffsets[candidate.LiteralId];
        if (literalOffset != 0)
        {
            int literalStart = start + 1;
            if (literalOffset > haystack.Length - literalStart ||
                !haystack.Slice(literalStart, literalOffset).SequenceEqual(branch.Literal.AsSpan(0, literalOffset)))
            {
                return false;
            }

            if (searchPatternsIncludeTrailingAtom)
            {
                length = branch.Literal.Length + 2;
                return true;
            }

            int adjustedEnd = literalStart + branch.Literal.Length;
            if (branch.TrailingAtom is { } adjustedTrailingAtom)
            {
                if (adjustedEnd >= haystack.Length ||
                    !AtomMatches(haystack[adjustedEnd], adjustedTrailingAtom, options))
                {
                    return false;
                }

                adjustedEnd++;
            }

            length = adjustedEnd - start;
            return true;
        }

        int end = start + 1 + candidate.Match.Length;
        if (!searchPatternsIncludeTrailingAtom && branch.TrailingAtom is { } trailingAtom)
        {
            end = start + 1 + branch.Literal.Length;
            if (end >= haystack.Length || !AtomMatches(haystack[end], trailingAtom, options))
            {
                return false;
            }

            end++;
        }

        length = end - start;
        return true;
    }

    private int CandidateMatchStart(RegexLiteralSetCandidate candidate)
    {
        return candidate.Match.Start - searchPatternLiteralOffsets[candidate.LiteralId] - 1;
    }

    private RegexMatch? FindByBranchLiteralScanner(ReadOnlySpan<byte> haystack, int lowerBound)
    {
        int searchAt = Math.Min(haystack.Length, lowerBound + 1);
        while (true)
        {
            RegexLiteralSetCandidate? found = branchLiteralScanner!.Find(haystack, searchAt);
            if (!found.HasValue)
            {
                return null;
            }

            RegexLiteralSetCandidate candidate = found.Value;
            int start = candidate.Match.Start - 1;
            if (start >= lowerBound &&
                TryMatchBranchAt(haystack, start, candidate.LiteralId, out int length))
            {
                return new RegexMatch(start, length);
            }

            searchAt = candidate.Match.Start + 1;
        }
    }

    private long CountOrSumByBranchLiteralScanner(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        long total = 0;
        int offset = startOffset;
        int searchAt = Math.Min(haystack.Length, offset + 1);
        while (true)
        {
            RegexLiteralSetCandidate? found = branchLiteralScanner!.Find(haystack, searchAt);
            if (!found.HasValue)
            {
                return total;
            }

            RegexLiteralSetCandidate candidate = found.Value;
            int start = candidate.Match.Start - 1;
            if (start >= offset &&
                TryMatchBranchAt(haystack, start, candidate.LiteralId, out int length))
            {
                total += sumSpans ? length : 1;
                offset = start + length;
                searchAt = Math.Min(haystack.Length, offset + 1);
            }
            else
            {
                searchAt = candidate.Match.Start + 1;
            }
        }
    }

    private long CountOrSumByTrailingAnchor(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        long total = 0;
        int nextAllowedStart = startOffset;
        int searchAt = Math.Min(haystack.Length, nextAllowedStart + trailingAnchorScanner!.MinLiteralLength);
        RegexMatch? best = null;
        while (true)
        {
            int anchor = trailingAnchorScanner.Find(haystack, searchAt);
            if (anchor < 0)
            {
                break;
            }

            if (best.HasValue && anchor - trailingAnchorScanner.MaxLiteralLength > best.Value.Start)
            {
                AddMatch(best.Value, sumSpans, ref total, ref nextAllowedStart);
                best = null;
                searchAt = Math.Min(haystack.Length, nextAllowedStart + trailingAnchorScanner.MinLiteralLength);
                continue;
            }

            for (int index = 0; index < branches.Length; index++)
            {
                int start = anchor - branches[index].Literal.Length;
                if (start < nextAllowedStart ||
                    (best.HasValue && start >= best.Value.Start))
                {
                    continue;
                }

                if (TryMatchAt(haystack, start, out int length))
                {
                    best = new RegexMatch(start, length);
                }
            }

            searchAt = anchor + 1;
        }

        if (best.HasValue)
        {
            AddMatch(best.Value, sumSpans, ref total, ref nextAllowedStart);
        }

        return total;
    }

    private static void AddMatch(RegexMatch match, bool sumSpans, ref long total, ref int nextAllowedStart)
    {
        total += sumSpans ? match.Length : 1;
        nextAllowedStart = match.End;
    }

    private bool TryMatchBranchAt(ReadOnlySpan<byte> haystack, int start, int branchId, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            (uint)branchId >= (uint)branches.Length)
        {
            return false;
        }

        RegexLeadingClassLiteralBranch branch = branches[branchId];
        if (!LeadingByteMatches(haystack[start], branch.LeadingKind) ||
            branch.Literal.Length > haystack.Length - start - 1 ||
            !haystack.Slice(start + 1, branch.Literal.Length).SequenceEqual(branch.Literal))
        {
            return false;
        }

        int end = start + 1 + branch.Literal.Length;
        if (branch.TrailingAtom is { } trailingAtom)
        {
            if (end >= haystack.Length || !AtomMatches(haystack[end], trailingAtom, options))
            {
                return false;
            }

            end++;
        }

        length = end - start;
        return true;
    }

    private RegexMatch? FindByTrailingAnchor(ReadOnlySpan<byte> haystack, int lowerBound)
    {
        int searchAt = Math.Min(haystack.Length, lowerBound + trailingAnchorScanner!.MinLiteralLength);
        RegexMatch? best = null;
        while (true)
        {
            int anchor = trailingAnchorScanner.Find(haystack, searchAt);
            if (anchor < 0)
            {
                return best;
            }

            if (best.HasValue && anchor - trailingAnchorScanner.MaxLiteralLength > best.Value.Start)
            {
                return best;
            }

            for (int index = 0; index < branches.Length; index++)
            {
                int start = anchor - branches[index].Literal.Length;
                if (start < lowerBound ||
                    (best.HasValue && start >= best.Value.Start))
                {
                    continue;
                }

                if (TryMatchAt(haystack, start, out int length))
                {
                    best = new RegexMatch(start, length);
                }
            }

            searchAt = anchor + 1;
        }
    }

    private bool TryFindLiteral(ReadOnlySpan<byte> haystack, int startAt, out RegexLiteralSetCandidate candidate)
    {
        if (scanner is not null)
        {
            RegexLiteralSetCandidate? found = scanner.Find(haystack, startAt);
            candidate = found.GetValueOrDefault();
            return found.HasValue;
        }

        if (packedScanner is not null)
        {
            RegexLiteralSetCandidate? found = packedScanner.Find(haystack, startAt);
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
        int offset = singleLiteralFinder!.Find(haystack[start..]);
        if (offset < 0)
        {
            candidate = default;
            return false;
        }

        candidate = new RegexLiteralSetCandidate(0, new RegexMatch(start + offset, searchPatterns[0].Length));
        return true;
    }

    private static bool AtomMatches(byte value, RegexAtomSpec atom, RegexCompileOptions options)
    {
        return RegexByteClass.AtomMatches(
            value,
            atom.Kind,
            atom.Value,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator);
    }

    private static bool TrySplitOuterTrailingAtom(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexSyntaxNode branchRoot,
        out RegexAtomSpec? trailingAtom)
    {
        branchRoot = root;
        trailingAtom = null;
        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            !TryGetAtomSpec(sequence.Nodes[1], options, out RegexAtomSpec candidateTrailing))
        {
            return false;
        }

        branchRoot = sequence.Nodes[0];
        trailingAtom = candidateTrailing;
        return true;
    }

    private static bool TryGetBranch(
        RegexSyntaxNode node,
        RegexAtomSpec? trailingAtom,
        out RegexLeadingClassLiteralBranch branch)
    {
        branch = default;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexSequenceNode { Nodes.Count: >= 2 } sequence ||
            !TryGetLeadingKind(sequence.Nodes[0], out RegexLeadingClassLiteralKind leadingKind))
        {
            return false;
        }

        var literal = new List<byte>();
        for (int index = 1; index < sequence.Nodes.Count; index++)
        {
            if (!TryAppendAsciiLiteral(sequence.Nodes[index], literal))
            {
                return false;
            }
        }

        if (literal.Count == 0)
        {
            return false;
        }

        branch = new RegexLeadingClassLiteralBranch(leadingKind, literal.ToArray(), trailingAtom);
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

    private static bool TryGetLeadingKind(RegexSyntaxNode node, out RegexLeadingClassLiteralKind leadingKind)
    {
        leadingKind = RegexLeadingClassLiteralKind.AsciiLetter;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode atom)
        {
            return false;
        }

        if (atom.Kind == RegexSyntaxKind.LetterClass)
        {
            return true;
        }

        if (atom.Kind != RegexSyntaxKind.CharacterClass)
        {
            return false;
        }

        ReadOnlySpan<byte> value = atom.Value.Span;
        if (value.SequenceEqual("a-z"u8))
        {
            leadingKind = RegexLeadingClassLiteralKind.AsciiLowercase;
            return true;
        }

        if (value.SequenceEqual("A-Z"u8))
        {
            leadingKind = RegexLeadingClassLiteralKind.AsciiUppercase;
            return true;
        }

        if (value.SequenceEqual("A-Za-z"u8) || value.SequenceEqual("a-zA-Z"u8))
        {
            leadingKind = RegexLeadingClassLiteralKind.AsciiLetter;
            return true;
        }

        return false;
    }

    private static bool TryGetAtomSpec(RegexSyntaxNode node, RegexCompileOptions options, out RegexAtomSpec atom)
    {
        atom = default;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode candidate ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                candidate.Kind,
                candidate.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        atom = new RegexAtomSpec(candidate.Kind, candidate.Value.ToArray());
        return true;
    }

    private static bool LeadingByteMatches(byte value, RegexLeadingClassLiteralKind leadingKind)
    {
        return leadingKind switch
        {
            RegexLeadingClassLiteralKind.AsciiLowercase => RegexSimpleSequenceSegment.IsAsciiLowercase(value),
            RegexLeadingClassLiteralKind.AsciiUppercase => RegexSimpleSequenceSegment.IsAsciiUppercase(value),
            _ => RegexSimpleSequenceSegment.IsAsciiLetter(value),
        };
    }

    private static byte[][] GetLiterals(List<RegexLeadingClassLiteralBranch> branches)
    {
        byte[][] literals = new byte[branches.Count][];
        for (int index = 0; index < branches.Count; index++)
        {
            literals[index] = branches[index].Literal;
        }

        return literals;
    }

    private static int[] BuildBranchIds(int count)
    {
        int[] branchIds = new int[count];
        for (int index = 0; index < branchIds.Length; index++)
        {
            branchIds[index] = index;
        }

        return branchIds;
    }

    private static byte[][]? TryBuildTrailingSearchPatterns(
        List<RegexLeadingClassLiteralBranch> branches,
        RegexCompileOptions options,
        bool preferTrailingWindow,
        out int[]? branchIds,
        out int[]? literalOffsets)
    {
        List<byte[]> patterns = [];
        List<int> patternBranchIds = [];
        List<int> patternLiteralOffsets = [];
        for (int branchIndex = 0; branchIndex < branches.Count; branchIndex++)
        {
            RegexLeadingClassLiteralBranch branch = branches[branchIndex];
            if (!branch.TrailingAtom.HasValue ||
                !TryGetAtomBytes(branch.TrailingAtom.Value, options, out byte[] trailingBytes) ||
                trailingBytes.Length == 0 ||
                trailingBytes.Length > MaxTrailingSearchBytes)
            {
                branchIds = null;
                literalOffsets = null;
                return null;
            }

            for (int byteIndex = 0; byteIndex < trailingBytes.Length; byteIndex++)
            {
                int fullLength = branch.Literal.Length + 1;
                int patternLength = preferTrailingWindow
                    ? Math.Min(TrailingSearchWindowLength, fullLength)
                    : fullLength;
                int literalOffset = fullLength - patternLength;
                byte[] pattern = new byte[patternLength];
                for (int patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
                {
                    int sourceIndex = literalOffset + patternIndex;
                    pattern[patternIndex] = sourceIndex < branch.Literal.Length
                        ? branch.Literal[sourceIndex]
                        : trailingBytes[byteIndex];
                }

                patterns.Add(pattern);
                patternBranchIds.Add(branchIndex);
                patternLiteralOffsets.Add(literalOffset);
                if (patterns.Count > MaxExpandedSearchPatternCount)
                {
                    branchIds = null;
                    literalOffsets = null;
                    return null;
                }
            }
        }

        if (patterns.Count == 0)
        {
            branchIds = null;
            literalOffsets = null;
            return null;
        }

        branchIds = patternBranchIds.ToArray();
        literalOffsets = patternLiteralOffsets.ToArray();
        return patterns.ToArray();
    }

    private static bool HasNonZeroOffset(int[] offsets)
    {
        for (int index = 0; index < offsets.Length; index++)
        {
            if (offsets[index] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SearchPatternCandidateBranchesAreUnambiguous(byte[][] searchPatterns)
    {
        for (int left = 0; left < searchPatterns.Length; left++)
        {
            for (int right = left + 1; right < searchPatterns.Length; right++)
            {
                if (OneSearchPatternCanMatchTheOther(searchPatterns[left], searchPatterns[right]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool OneSearchPatternCanMatchTheOther(byte[] left, byte[] right)
    {
        int length = Math.Min(left.Length, right.Length);
        for (int index = 0; index < length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetAtomBytes(RegexAtomSpec atom, RegexCompileOptions options, out byte[] bytes)
    {
        List<byte> matches = [];
        for (int value = 0; value <= 0xFF; value++)
        {
            if (AtomMatches((byte)value, atom, options))
            {
                matches.Add((byte)value);
                if (matches.Count > MaxTrailingSearchBytes)
                {
                    bytes = [];
                    return false;
                }
            }
        }

        bytes = matches.ToArray();
        return true;
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
