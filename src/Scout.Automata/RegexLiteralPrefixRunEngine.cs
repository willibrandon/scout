namespace Scout;

internal sealed class RegexLiteralPrefixRunEngine
{
    private const int MaxPrefixCount = 8;

    private readonly RegexLiteralPrefixRunBranch[] branches;
    private readonly bool asciiCaseInsensitive;
    private readonly RegexShortLiteralSetScanner? shortScanner;
    private readonly RegexCaseSensitiveLiteralSetScanner? caseSensitiveScanner;
    private readonly RegexAsciiCaseInsensitiveLiteralSetScanner? asciiCaseInsensitiveScanner;
    private readonly MemmemFinder? singleLiteralFinder;
    private readonly RegexAsciiCaseInsensitiveFinder? singleAsciiCaseInsensitiveFinder;

    private RegexLiteralPrefixRunEngine(
        RegexLiteralPrefixRunBranch[] branches,
        bool asciiCaseInsensitive,
        RegexShortLiteralSetScanner? shortScanner,
        RegexCaseSensitiveLiteralSetScanner? caseSensitiveScanner)
    {
        this.branches = branches;
        this.asciiCaseInsensitive = asciiCaseInsensitive;
        this.shortScanner = shortScanner;
        this.caseSensitiveScanner = caseSensitiveScanner;
        if (asciiCaseInsensitive)
        {
            if (branches.Length == 1)
            {
                singleAsciiCaseInsensitiveFinder = new RegexAsciiCaseInsensitiveFinder(branches[0].Prefix);
            }
            else
            {
                asciiCaseInsensitiveScanner = new RegexAsciiCaseInsensitiveLiteralSetScanner(GetPrefixes(branches));
            }
        }
        else if (branches.Length == 1)
        {
            singleLiteralFinder = new MemmemFinder(branches[0].Prefix);
        }
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexLiteralPrefixRunEngine? engine)
    {
        engine = null;
        if (options.Utf8 ||
            options.UnicodeClasses ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        var branches = new List<RegexLiteralPrefixRunBranch>();
        if (root is RegexAlternationNode alternation)
        {
            if (alternation.Alternatives.Count == 0 || alternation.Alternatives.Count > MaxPrefixCount)
            {
                return false;
            }

            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryGetBranch(alternation.Alternatives[index], options, out RegexLiteralPrefixRunBranch branch))
                {
                    return false;
                }

                branches.Add(branch);
            }
        }
        else if (!TryGetBranch(root, options, out RegexLiteralPrefixRunBranch branch))
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

        RegexShortLiteralSetScanner? shortScanner = null;
        RegexCaseSensitiveLiteralSetScanner? caseSensitiveScanner = null;
        byte[][] prefixes = GetPrefixes(branches);
        if (!options.CaseInsensitive &&
            branches.Count > 1 &&
            !RegexShortLiteralSetScanner.TryCreate(prefixes, out shortScanner) &&
            !RegexCaseSensitiveLiteralSetScanner.TryCreate(prefixes, out caseSensitiveScanner))
        {
            return false;
        }

        engine = new RegexLiteralPrefixRunEngine(
            branches.ToArray(),
            options.CaseInsensitive,
            shortScanner,
            caseSensitiveScanner);
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
            RegexLiteralPrefixRunBranch branch = branches[index];
            if (!PrefixMatchesAt(haystack, start, branch.Prefix))
            {
                continue;
            }

            int runStart = start + branch.Prefix.Length;
            if (runStart >= haystack.Length || !RunByteMatches(haystack[runStart], branch.RunKind))
            {
                continue;
            }

            int end = runStart + 1;
            while (end < haystack.Length && RunByteMatches(haystack[end], branch.RunKind))
            {
                end++;
            }

            length = end - start;
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

    private bool TryFindPrefix(ReadOnlySpan<byte> haystack, int startAt, out RegexLiteralSetCandidate candidate)
    {
        if (shortScanner is not null)
        {
            RegexLiteralSetCandidate? found = shortScanner.Find(haystack, startAt);
            candidate = found.GetValueOrDefault();
            return found.HasValue;
        }

        if (caseSensitiveScanner is not null)
        {
            RegexLiteralSetCandidate? found = caseSensitiveScanner.Find(haystack, startAt);
            candidate = found.GetValueOrDefault();
            return found.HasValue;
        }

        if (asciiCaseInsensitiveScanner is not null)
        {
            RegexLiteralSetCandidate? found = asciiCaseInsensitiveScanner.Find(haystack, startAt);
            candidate = found.GetValueOrDefault();
            return found.HasValue;
        }

        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (singleLiteralFinder is not null)
        {
            int offset = singleLiteralFinder.Find(haystack[start..]);
            if (offset < 0)
            {
                candidate = default;
                return false;
            }

            candidate = new RegexLiteralSetCandidate(0, new RegexMatch(start + offset, branches[0].Prefix.Length));
            return true;
        }

        int caseInsensitiveOffset = singleAsciiCaseInsensitiveFinder!.Find(haystack[start..]);
        if (caseInsensitiveOffset < 0)
        {
            candidate = default;
            return false;
        }

        candidate = new RegexLiteralSetCandidate(
            0,
            new RegexMatch(start + caseInsensitiveOffset, branches[0].Prefix.Length));
        return true;
    }

    private bool PrefixMatchesAt(ReadOnlySpan<byte> haystack, int start, byte[] prefix)
    {
        if (prefix.Length > haystack.Length - start)
        {
            return false;
        }

        if (!asciiCaseInsensitive)
        {
            return haystack.Slice(start, prefix.Length).SequenceEqual(prefix);
        }

        for (int index = 0; index < prefix.Length; index++)
        {
            if (RegexAsciiCaseInsensitiveFinder.FoldAscii(haystack[start + index]) !=
                RegexAsciiCaseInsensitiveFinder.FoldAscii(prefix[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetBranch(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexLiteralPrefixRunBranch branch)
    {
        branch = default;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexSequenceNode { Nodes.Count: >= 2 } sequence)
        {
            return false;
        }

        var prefix = new List<byte>();
        for (int index = 0; index < sequence.Nodes.Count - 1; index++)
        {
            RegexSyntaxNode child = UnwrapTransparentGroups(sequence.Nodes[index]);
            if (child is RegexInlineFlagsNode ||
                child is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom ||
                !AppendAsciiLiteral(prefix, atom.Value.Span))
            {
                return false;
            }
        }

        if (prefix.Count == 0 ||
            !TryGetRepeatedAsciiLetterRunKind(sequence.Nodes[^1], options, out RegexLiteralPrefixRunKind runKind))
        {
            return false;
        }

        branch = new RegexLiteralPrefixRunBranch(prefix.ToArray(), runKind);
        return true;
    }

    private static bool TryGetRepeatedAsciiLetterRunKind(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexLiteralPrefixRunKind runKind)
    {
        runKind = RegexLiteralPrefixRunKind.AsciiLetter;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: null,
                Lazy: false,
            } repetition)
        {
            return false;
        }

        RegexSyntaxNode child = UnwrapTransparentGroups(repetition.Child);
        return child is RegexAtomNode atom && TryGetAsciiLetterRunKind(atom, options.CaseInsensitive, out runKind);
    }

    private static bool TryGetAsciiLetterRunKind(
        RegexAtomNode atom,
        bool caseInsensitive,
        out RegexLiteralPrefixRunKind runKind)
    {
        runKind = RegexLiteralPrefixRunKind.AsciiLetter;
        if (atom.Kind == RegexSyntaxKind.LetterClass)
        {
            return true;
        }

        if (atom.Kind != RegexSyntaxKind.CharacterClass)
        {
            return false;
        }

        ReadOnlySpan<byte> value = atom.Value.Span;
        if (IsAsciiLetterClass(value))
        {
            runKind = RegexLiteralPrefixRunKind.AsciiLetter;
            return true;
        }

        if (IsAsciiLowercaseClass(value))
        {
            runKind = caseInsensitive
                ? RegexLiteralPrefixRunKind.AsciiLetter
                : RegexLiteralPrefixRunKind.AsciiLowercase;
            return true;
        }

        if (IsAsciiUppercaseClass(value))
        {
            runKind = caseInsensitive
                ? RegexLiteralPrefixRunKind.AsciiLetter
                : RegexLiteralPrefixRunKind.AsciiUppercase;
            return true;
        }

        return false;
    }

    private static bool AppendAsciiLiteral(List<byte> prefix, ReadOnlySpan<byte> literal)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            if (literal[index] > 0x7F)
            {
                return false;
            }

            prefix.Add(literal[index]);
        }

        return true;
    }

    private static bool RunByteMatches(byte value, RegexLiteralPrefixRunKind runKind)
    {
        return runKind switch
        {
            RegexLiteralPrefixRunKind.AsciiLowercase => RegexSimpleSequenceSegment.IsAsciiLowercase(value),
            RegexLiteralPrefixRunKind.AsciiUppercase => RegexSimpleSequenceSegment.IsAsciiUppercase(value),
            _ => RegexSimpleSequenceSegment.IsAsciiLetter(value),
        };
    }

    private static bool IsAsciiLowercaseClass(ReadOnlySpan<byte> value)
    {
        return value.SequenceEqual("a-z"u8);
    }

    private static bool IsAsciiUppercaseClass(ReadOnlySpan<byte> value)
    {
        return value.SequenceEqual("A-Z"u8);
    }

    private static bool IsAsciiLetterClass(ReadOnlySpan<byte> value)
    {
        return value.SequenceEqual("A-Za-z"u8) || value.SequenceEqual("a-zA-Z"u8);
    }

    private static byte[][] GetPrefixes(IReadOnlyList<RegexLiteralPrefixRunBranch> branches)
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
