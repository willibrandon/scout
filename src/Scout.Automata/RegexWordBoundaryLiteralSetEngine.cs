namespace Scout;

internal sealed class RegexWordBoundaryLiteralSetEngine
{
    private const int MaxLiteralCount = 4096;
    private const int MaxLiteralLength = 128;
    private const int MaxClassLiteralCount = 32;

    private readonly AhoCorasickAutomaton automaton;
    private readonly byte[][] literals;
    private readonly int[][] literalIndexesByFirstByte;
    private readonly RegexCompileOptions leadingBoundaryOptions;
    private readonly RegexCompileOptions trailingBoundaryOptions;
    private readonly int maxLiteralLength;
    private readonly bool asciiBoundaryFastPath;

    private RegexWordBoundaryLiteralSetEngine(
        IReadOnlyList<byte[]> literals,
        RegexCompileOptions leadingBoundaryOptions,
        RegexCompileOptions trailingBoundaryOptions)
    {
        this.literals = new byte[literals.Count][];
        for (int index = 0; index < literals.Count; index++)
        {
            this.literals[index] = literals[index].ToArray();
            maxLiteralLength = Math.Max(maxLiteralLength, this.literals[index].Length);
        }

        this.leadingBoundaryOptions = leadingBoundaryOptions;
        this.trailingBoundaryOptions = trailingBoundaryOptions;
        asciiBoundaryFastPath = !leadingBoundaryOptions.Utf8 &&
            !leadingBoundaryOptions.UnicodeClasses &&
            !trailingBoundaryOptions.Utf8 &&
            !trailingBoundaryOptions.UnicodeClasses;
        literalIndexesByFirstByte = BuildLiteralBuckets(this.literals);
        automaton = AhoCorasickAutomaton.Create(this.literals, AhoCorasickMatchKind.Standard);
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexWordBoundaryLiteralSetEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive)
        {
            return false;
        }

        if (!TryGetWordBoundaryLiteralSet(
                root,
                options,
                out List<byte[]> literals,
                out RegexCompileOptions leadingBoundaryOptions,
                out RegexCompileOptions trailingBoundaryOptions) ||
            literals.Count == 0)
        {
            return false;
        }

        engine = new RegexWordBoundaryLiteralSetEngine(literals, leadingBoundaryOptions, trailingBoundaryOptions);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        AhoCorasickOverlappingEnumerator matches = automaton.EnumerateOverlapping(haystack[startOffset..]);
        RegexLiteralSetCandidate? best = null;
        while (matches.MoveNext())
        {
            AhoCorasickMatch match = matches.Current;
            if (best.HasValue &&
                startOffset + match.End > best.Value.Match.Start + maxLiteralLength)
            {
                break;
            }

            RegexLiteralSetCandidate candidate = ResolveCandidate(startOffset, match);
            if (!HasWordBoundaries(haystack, candidate.Match.Start, candidate.Match.Length) ||
                !IsBetter(candidate, best))
            {
                continue;
            }

            best = candidate;
        }

        return best.HasValue ? best.Value.Match : null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (start >= haystack.Length)
        {
            return null;
        }

        ReadOnlySpan<int> candidates = literalIndexesByFirstByte[haystack[start]];
        for (int index = 0; index < candidates.Length; index++)
        {
            int literalId = candidates[index];
            byte[] literal = literals[literalId];
            if (literal.Length <= haystack.Length - start &&
                haystack.Slice(start, literal.Length).SequenceEqual(literal) &&
                HasWordBoundaries(haystack, start, literal.Length))
            {
                return new RegexMatch(start, literal.Length);
            }
        }

        return null;
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
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        int nextAllowedStart = startOffset;
        long total = 0;
        var pending = new List<RegexLiteralSetCandidate>();
        AhoCorasickOverlappingEnumerator matches = automaton.EnumerateOverlapping(haystack[startOffset..]);
        while (matches.MoveNext())
        {
            AhoCorasickMatch ahoMatch = matches.Current;
            RegexLiteralSetCandidate candidate = ResolveCandidate(startOffset, ahoMatch);
            if (candidate.Match.Start >= nextAllowedStart &&
                HasWordBoundaries(haystack, candidate.Match.Start, candidate.Match.Length))
            {
                pending.Add(candidate);
            }

            DrainResolvedCandidates(
                pending,
                startOffset + ahoMatch.End,
                ref nextAllowedStart,
                sumSpans,
                ref total);
        }

        while (pending.Count != 0)
        {
            AcceptBestCandidate(pending, ref nextAllowedStart, sumSpans, ref total);
        }

        return total;
    }

    private void DrainResolvedCandidates(
        List<RegexLiteralSetCandidate> pending,
        int observedEnd,
        ref int nextAllowedStart,
        bool sumSpans,
        ref long total)
    {
        while (pending.Count != 0)
        {
            int earliestStart = int.MaxValue;
            for (int index = 0; index < pending.Count; index++)
            {
                earliestStart = Math.Min(earliestStart, pending[index].Match.Start);
            }

            if (observedEnd <= earliestStart + maxLiteralLength)
            {
                return;
            }

            AcceptBestCandidate(pending, ref nextAllowedStart, sumSpans, ref total);
        }
    }

    private static void AcceptBestCandidate(
        List<RegexLiteralSetCandidate> pending,
        ref int nextAllowedStart,
        bool sumSpans,
        ref long total)
    {
        int bestIndex = -1;
        RegexLiteralSetCandidate best = default;
        for (int index = 0; index < pending.Count; index++)
        {
            RegexLiteralSetCandidate candidate = pending[index];
            if (candidate.Match.Start < nextAllowedStart)
            {
                continue;
            }

            if (bestIndex < 0 || IsBetter(candidate, best))
            {
                bestIndex = index;
                best = candidate;
            }
        }

        if (bestIndex < 0)
        {
            pending.Clear();
            return;
        }

        RegexMatch match = best.Match;
        total += sumSpans ? match.Length : 1;
        nextAllowedStart = match.End;
        RemovePendingBefore(pending, nextAllowedStart);
    }

    private static void RemovePendingBefore(List<RegexLiteralSetCandidate> pending, int start)
    {
        int write = 0;
        for (int read = 0; read < pending.Count; read++)
        {
            RegexLiteralSetCandidate candidate = pending[read];
            if (candidate.Match.Start >= start)
            {
                pending[write] = candidate;
                write++;
            }
        }

        if (write < pending.Count)
        {
            pending.RemoveRange(write, pending.Count - write);
        }
    }

    private static RegexLiteralSetCandidate ResolveCandidate(int startOffset, AhoCorasickMatch match)
    {
        int start = startOffset + match.Start;
        return new RegexLiteralSetCandidate(match.PatternId, new RegexMatch(start, match.Length));
    }

    private bool HasWordBoundaries(ReadOnlySpan<byte> haystack, int start, int length)
    {
        int end = start + length;
        if (length <= 0 ||
            start < 0 ||
            end > haystack.Length)
        {
            return false;
        }

        if (asciiBoundaryFastPath ||
            HasAsciiBoundaryContext(haystack, start, end))
        {
            return HasAsciiWordBoundaries(haystack, start, end);
        }

        return length > 0 &&
            RegexByteClass.PredicateMatches(
                haystack,
                start,
                RegexSyntaxKind.WordBoundary,
                leadingBoundaryOptions.MultiLine,
                leadingBoundaryOptions.Crlf,
                leadingBoundaryOptions.LineTerminator,
                leadingBoundaryOptions.Utf8,
                leadingBoundaryOptions.UnicodeClasses) &&
            RegexByteClass.PredicateMatches(
                haystack,
                end,
                RegexSyntaxKind.WordBoundary,
                trailingBoundaryOptions.MultiLine,
                trailingBoundaryOptions.Crlf,
                trailingBoundaryOptions.LineTerminator,
                trailingBoundaryOptions.Utf8,
                trailingBoundaryOptions.UnicodeClasses);
    }

    private static bool HasAsciiBoundaryContext(ReadOnlySpan<byte> haystack, int start, int end)
    {
        return (start == 0 || haystack[start - 1] <= 0x7F) &&
            (end == haystack.Length || haystack[end] <= 0x7F);
    }

    private static bool HasAsciiWordBoundaries(ReadOnlySpan<byte> haystack, int start, int end)
    {
        return (start == 0 || !IsAsciiWord(haystack[start - 1])) &&
            (end == haystack.Length || !IsAsciiWord(haystack[end]));
    }

    private static bool TryGetWordBoundaryLiteralSet(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out List<byte[]> literals,
        out RegexCompileOptions leadingBoundaryOptions,
        out RegexCompileOptions trailingBoundaryOptions)
    {
        literals = [];
        leadingBoundaryOptions = options;
        trailingBoundaryOptions = options;
        if (!TryUnwrapWithOptions(root, options, out root, out options) ||
            !TryCollectWordBoundaryLiteralSet(root, options, literals, ref leadingBoundaryOptions, ref trailingBoundaryOptions) ||
            literals.Count == 0)
        {
            return false;
        }

        for (int index = 0; index < literals.Count; index++)
        {
            byte[] literal = literals[index];
            if (literal.Length == 0 ||
                literal.Length > MaxLiteralLength ||
                !IsAsciiWordLiteral(literal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryCollectWordBoundaryLiteralSet(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        List<byte[]> literals,
        ref RegexCompileOptions leadingBoundaryOptions,
        ref RegexCompileOptions trailingBoundaryOptions)
    {
        if (root is RegexAlternationNode alternation)
        {
            bool sawBranch = false;
            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryCollectWordBoundaryBranch(
                        alternation.Alternatives[index],
                        options,
                        literals,
                        ref leadingBoundaryOptions,
                        ref trailingBoundaryOptions,
                        ref sawBranch))
                {
                    return false;
                }
            }

            return sawBranch;
        }

        bool sawSingle = false;
        return TryCollectWordBoundaryBranch(
            root,
            options,
            literals,
            ref leadingBoundaryOptions,
            ref trailingBoundaryOptions,
            ref sawSingle);
    }

    private static bool TryCollectWordBoundaryBranch(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte[]> literals,
        ref RegexCompileOptions leadingBoundaryOptions,
        ref RegexCompileOptions trailingBoundaryOptions,
        ref bool sawBranch)
    {
        if (!TryUnwrapWithOptions(node, options, out node, out options) ||
            node is not RegexSequenceNode sequence ||
            sequence.Nodes.Count != 3 ||
            !TryGetWordBoundaryOptions(sequence.Nodes[0], options, out RegexCompileOptions branchLeadingBoundaryOptions) ||
            !TryGetWordBoundaryOptions(sequence.Nodes[2], options, out RegexCompileOptions branchTrailingBoundaryOptions))
        {
            return false;
        }

        if (!sawBranch)
        {
            leadingBoundaryOptions = branchLeadingBoundaryOptions;
            trailingBoundaryOptions = branchTrailingBoundaryOptions;
            sawBranch = true;
        }
        else if (!SameBoundaryOptions(leadingBoundaryOptions, branchLeadingBoundaryOptions) ||
            !SameBoundaryOptions(trailingBoundaryOptions, branchTrailingBoundaryOptions))
        {
            return false;
        }

        return TryExpandLiteralLanguage(sequence.Nodes[1], options, literals);
    }

    private static bool SameBoundaryOptions(RegexCompileOptions left, RegexCompileOptions right)
    {
        return left.CaseInsensitive == right.CaseInsensitive &&
            left.MultiLine == right.MultiLine &&
            left.Crlf == right.Crlf &&
            left.LineTerminator == right.LineTerminator &&
            left.Utf8 == right.Utf8 &&
            left.UnicodeClasses == right.UnicodeClasses;
    }

    private static bool TryExpandLiteralLanguage(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte[]> literals)
    {
        List<byte[]> expanded = [Array.Empty<byte>()];
        if (!TryExpand(node, options, expanded))
        {
            return false;
        }

        for (int index = 0; index < expanded.Count; index++)
        {
            AddLiteral(literals, expanded[index]);
        }

        return true;
    }

    private static bool TryExpand(RegexSyntaxNode node, RegexCompileOptions options, List<byte[]> outputs)
    {
        if (!TryUnwrapWithOptions(node, options, out node, out options) ||
            options.CaseInsensitive)
        {
            return false;
        }

        switch (node)
        {
            case RegexEmptyNode:
                return true;
            case RegexInlineFlagsNode flags:
                return !options.Apply(flags.EnabledFlags, flags.DisabledFlags).CaseInsensitive;
            case RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom:
                return AppendBytes(outputs, atom.Value.Span);
            case RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom:
                return AppendCharacterClass(outputs, atom.Value.Span, options);
            case RegexSequenceNode sequence:
                return ExpandSequence(sequence, options, outputs);
            case RegexAlternationNode alternation:
                return ExpandAlternation(alternation, options, outputs);
            case RegexRepetitionNode repetition:
                return ExpandRepetition(repetition, options, outputs);
            default:
                return false;
        }
    }

    private static bool ExpandSequence(RegexSequenceNode sequence, RegexCompileOptions options, List<byte[]> outputs)
    {
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            if (!TryExpand(sequence.Nodes[index], options, outputs))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ExpandAlternation(RegexAlternationNode alternation, RegexCompileOptions options, List<byte[]> outputs)
    {
        List<byte[]> prefixes = Copy(outputs);
        outputs.Clear();
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            List<byte[]> branch = Copy(prefixes);
            if (!TryExpand(alternation.Alternatives[index], options, branch))
            {
                return false;
            }

            if (!AppendAll(outputs, branch))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ExpandRepetition(RegexRepetitionNode repetition, RegexCompileOptions options, List<byte[]> outputs)
    {
        if (repetition.Maximum is null ||
            repetition.Maximum.Value < repetition.Minimum ||
            repetition.Maximum.Value > 4)
        {
            return false;
        }

        List<byte[]> repeatedOnce = [Array.Empty<byte>()];
        if (!TryExpand(repetition.Child, options, repeatedOnce) ||
            repeatedOnce.Count == 0)
        {
            return false;
        }

        int minimum = repetition.Minimum;
        int maximum = repetition.Maximum.Value;
        List<byte[]> expanded = [];
        if (repetition.Lazy)
        {
            for (int count = minimum; count <= maximum; count++)
            {
                if (!AppendRepetitionCount(outputs, repeatedOnce, count, expanded))
                {
                    return false;
                }
            }
        }
        else
        {
            for (int count = maximum; count >= minimum; count--)
            {
                if (!AppendRepetitionCount(outputs, repeatedOnce, count, expanded))
                {
                    return false;
                }
            }
        }

        outputs.Clear();
        return AppendAll(outputs, expanded);
    }

    private static bool AppendRepetitionCount(
        List<byte[]> prefixes,
        List<byte[]> repeatedOnce,
        int count,
        List<byte[]> outputs)
    {
        List<byte[]> repeated = [Array.Empty<byte>()];
        for (int index = 0; index < count; index++)
        {
            if (!AppendVariants(repeated, repeatedOnce))
            {
                return false;
            }
        }

        List<byte[]> branch = Copy(prefixes);
        if (!AppendVariants(branch, repeated))
        {
            return false;
        }

        return AppendAll(outputs, branch);
    }

    private static bool AppendCharacterClass(
        List<byte[]> outputs,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options)
    {
        var variants = new List<byte[]>();
        for (int value = 0; value <= 0x7F; value++)
        {
            if (!RegexByteClass.AtomMatches(
                    (byte)value,
                    RegexSyntaxKind.CharacterClass,
                    expression,
                    options.CaseInsensitive,
                    options.MultiLine,
                    options.DotMatchesNewline,
                    options.Crlf,
                    options.LineTerminator) ||
                !IsAsciiWord((byte)value))
            {
                continue;
            }

            variants.Add([(byte)value]);
            if (variants.Count > MaxClassLiteralCount)
            {
                return false;
            }
        }

        return variants.Count > 0 && AppendVariants(outputs, variants);
    }

    private static bool AppendBytes(List<byte[]> outputs, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || !IsAsciiWordLiteral(bytes))
        {
            return false;
        }

        var variants = new List<byte[]> { bytes.ToArray() };
        return AppendVariants(outputs, variants);
    }

    private static bool AppendVariants(List<byte[]> outputs, List<byte[]> variants)
    {
        var expanded = new List<byte[]>();
        for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
        {
            byte[] prefix = outputs[outputIndex];
            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                byte[] variant = variants[variantIndex];
                if (prefix.Length + variant.Length > MaxLiteralLength)
                {
                    return false;
                }

                byte[] combined = new byte[prefix.Length + variant.Length];
                prefix.CopyTo(combined, 0);
                variant.CopyTo(combined, prefix.Length);
                expanded.Add(combined);
                if (expanded.Count > MaxLiteralCount)
                {
                    return false;
                }
            }
        }

        outputs.Clear();
        outputs.AddRange(expanded);
        return true;
    }

    private static bool AppendAll(List<byte[]> destination, List<byte[]> source)
    {
        for (int index = 0; index < source.Count; index++)
        {
            destination.Add(source[index]);
            if (destination.Count > MaxLiteralCount)
            {
                return false;
            }
        }

        return true;
    }

    private static List<byte[]> Copy(List<byte[]> source)
    {
        var copy = new List<byte[]>(source.Count);
        for (int index = 0; index < source.Count; index++)
        {
            copy.Add(source[index].ToArray());
        }

        return copy;
    }

    private static void AddLiteral(List<byte[]> literals, byte[] literal)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].AsSpan().SequenceEqual(literal))
            {
                return;
            }
        }

        literals.Add(literal);
    }

    private static bool TryGetWordBoundaryOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexCompileOptions boundaryOptions)
    {
        return TryUnwrapWithOptions(node, options, out RegexSyntaxNode unwrapped, out boundaryOptions) &&
            !boundaryOptions.CaseInsensitive &&
            unwrapped is RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary };
    }

    private static bool TryUnwrapWithOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSyntaxNode unwrapped,
        out RegexCompileOptions effectiveOptions)
    {
        while (node is RegexGroupNode group &&
            string.IsNullOrEmpty(group.CaptureName))
        {
            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            node = group.Child;
        }

        unwrapped = node;
        effectiveOptions = options;
        return true;
    }

    private static bool IsBetter(RegexLiteralSetCandidate candidate, RegexLiteralSetCandidate? best)
    {
        if (!best.HasValue)
        {
            return true;
        }

        return IsBetter(candidate, best.Value);
    }

    private static bool IsBetter(RegexLiteralSetCandidate candidate, RegexLiteralSetCandidate best)
    {
        if (candidate.Match.Start != best.Match.Start)
        {
            return candidate.Match.Start < best.Match.Start;
        }

        return candidate.LiteralId < best.LiteralId;
    }

    private static bool IsAsciiWordLiteral(ReadOnlySpan<byte> literal)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            if (!IsAsciiWord(literal[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiWord(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_';
    }

    private static int[][] BuildLiteralBuckets(byte[][] literals)
    {
        var buckets = new List<int>[256];
        for (int index = 0; index < buckets.Length; index++)
        {
            buckets[index] = [];
        }

        for (int index = 0; index < literals.Length; index++)
        {
            buckets[literals[index][0]].Add(index);
        }

        int[][] indexed = new int[256][];
        for (int index = 0; index < indexed.Length; index++)
        {
            indexed[index] = buckets[index].ToArray();
        }

        return indexed;
    }
}
