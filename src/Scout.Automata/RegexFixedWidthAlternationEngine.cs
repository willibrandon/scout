namespace Scout;

internal sealed class RegexFixedWidthAlternationEngine
{
    private const int MinAlternativeCount = 2;
    private const int MaxAlternativeCount = 32;
    private const int MaxWidth = 64;
    private const int MinLiteralSeedLength = 2;

    private readonly RegexFixedWidthAtom[][] alternatives;
    private readonly RegexFixedWidthLiteralSeed[]? literalSeeds;
    private readonly bool[] anchorBytes;
    private readonly byte[] anchorNeedles;
    private readonly int width;
    private readonly int anchorIndex;

    private RegexFixedWidthAlternationEngine(
        RegexFixedWidthAtom[][] alternatives,
        RegexFixedWidthLiteralSeed[]? literalSeeds,
        bool[] anchorBytes,
        byte[] anchorNeedles,
        int width,
        int anchorIndex)
    {
        this.alternatives = alternatives;
        this.literalSeeds = literalSeeds;
        this.anchorBytes = anchorBytes;
        this.anchorNeedles = anchorNeedles;
        this.width = width;
        this.anchorIndex = anchorIndex;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        int captureCount,
        RegexCompileOptions options,
        out RegexFixedWidthAlternationEngine? engine)
    {
        engine = null;
        if (captureCount != 0 ||
            options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexAlternationNode alternation ||
            alternation.Alternatives.Count is < MinAlternativeCount or > MaxAlternativeCount)
        {
            return false;
        }

        var compiledAlternatives = new RegexFixedWidthAtom[alternation.Alternatives.Count][];
        int width = -1;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            var atoms = new List<RegexFixedWidthAtom>();
            if (!TryAppendAtoms(alternation.Alternatives[index], options, atoms) ||
                atoms.Count == 0 ||
                atoms.Count > MaxWidth)
            {
                return false;
            }

            if (width < 0)
            {
                width = atoms.Count;
            }
            else if (atoms.Count != width)
            {
                return false;
            }

            compiledAlternatives[index] = [.. atoms];
        }

        if (width <= 0 || !TryChooseAnchor(compiledAlternatives, width, out int anchorIndex, out bool[]? anchorBytes, out byte[]? needles))
        {
            return false;
        }

        engine = new RegexFixedWidthAlternationEngine(
            compiledAlternatives,
            TryCreateLiteralSeeds(compiledAlternatives, out RegexFixedWidthLiteralSeed[]? seeds) ? seeds : null,
            anchorBytes!,
            needles!,
            width,
            anchorIndex);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (literalSeeds is not null)
        {
            return FindWithLiteralSeeds(haystack, start);
        }

        while (TryFindAnchor(haystack, start, out int candidate))
        {
            if (TryMatchAt(haystack, candidate))
            {
                return new RegexMatch(candidate, width);
            }

            start = candidate + 1;
        }

        return null;
    }

    private RegexMatch? FindWithLiteralSeeds(ReadOnlySpan<byte> haystack, int minimumStart)
    {
        RegexFixedWidthLiteralSeed[] seeds = literalSeeds!;
        int bestStart = int.MaxValue;
        for (int index = 0; index < seeds.Length; index++)
        {
            if (TryFindSeedMatch(haystack, minimumStart, seeds[index], bestStart, out int candidate) &&
                candidate < bestStart)
            {
                bestStart = candidate;
            }
        }

        return bestStart == int.MaxValue ? null : new RegexMatch(bestStart, width);
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, start)
            ? new RegexMatch(start, width)
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

    private bool TryFindAnchor(ReadOnlySpan<byte> haystack, int minimumStart, out int candidate)
    {
        candidate = 0;
        int searchAt = minimumStart + anchorIndex;
        int lastAnchor = haystack.Length - (width - anchorIndex);
        while (searchAt <= lastAnchor)
        {
            int anchor = FindAnchorByte(haystack, searchAt, lastAnchor + 1);
            if (anchor < 0)
            {
                return false;
            }

            candidate = anchor - anchorIndex;
            if (candidate >= minimumStart)
            {
                return true;
            }

            searchAt = anchor + 1;
        }

        return false;
    }

    private bool TryFindSeedMatch(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        RegexFixedWidthLiteralSeed seed,
        int bestStart,
        out int candidate)
    {
        candidate = 0;
        int searchAt = minimumStart + seed.Offset;
        int lastSeedStart = haystack.Length - (width - seed.Offset);
        while (searchAt <= lastSeedStart && searchAt - seed.Offset < bestStart)
        {
            int offset = haystack[searchAt..].IndexOf(seed.Literal);
            if (offset < 0)
            {
                return false;
            }

            int seedStart = searchAt + offset;
            if (seedStart > lastSeedStart)
            {
                return false;
            }

            int matchStart = seedStart - seed.Offset;
            if (matchStart >= minimumStart &&
                TryMatchAlternative(seed.AlternativeIndex, haystack, matchStart))
            {
                candidate = matchStart;
                return true;
            }

            searchAt = seedStart + 1;
        }

        return false;
    }

    private int FindAnchorByte(ReadOnlySpan<byte> haystack, int start, int end)
    {
        if (anchorNeedles.Length == 1)
        {
            int offset = haystack[start..end].IndexOf(anchorNeedles[0]);
            return offset < 0 ? -1 : start + offset;
        }

        if (anchorNeedles.Length == 2)
        {
            int offset = haystack[start..end].IndexOfAny(anchorNeedles[0], anchorNeedles[1]);
            return offset < 0 ? -1 : start + offset;
        }

        if (anchorNeedles.Length == 3)
        {
            int offset = haystack[start..end].IndexOfAny(anchorNeedles[0], anchorNeedles[1], anchorNeedles[2]);
            return offset < 0 ? -1 : start + offset;
        }

        for (int index = start; index < end; index++)
        {
            if (anchorBytes[haystack[index]])
            {
                return index;
            }
        }

        return -1;
    }

    private bool TryMatchAt(ReadOnlySpan<byte> haystack, int start)
    {
        if (start > haystack.Length - width)
        {
            return false;
        }

        for (int alternativeIndex = 0; alternativeIndex < alternatives.Length; alternativeIndex++)
        {
            if (TryMatchAlternative(alternativeIndex, haystack, start))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryMatchAlternative(int alternativeIndex, ReadOnlySpan<byte> haystack, int start)
    {
        if (start > haystack.Length - width)
        {
            return false;
        }

        RegexFixedWidthAtom[] alternative = alternatives[alternativeIndex];
        for (int index = 0; index < alternative.Length; index++)
        {
            if (!alternative[index].Matches(haystack[start + index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryCreateLiteralSeeds(
        RegexFixedWidthAtom[][] alternatives,
        out RegexFixedWidthLiteralSeed[]? seeds)
    {
        var seedList = new RegexFixedWidthLiteralSeed[alternatives.Length];
        for (int index = 0; index < alternatives.Length; index++)
        {
            if (!TryGetLongestLiteralRun(alternatives[index], out int offset, out byte[]? literal) ||
                literal is null ||
                literal.Length < MinLiteralSeedLength)
            {
                seeds = null;
                return false;
            }

            seedList[index] = new RegexFixedWidthLiteralSeed(index, offset, literal);
        }

        seeds = seedList;
        return true;
    }

    private static bool TryGetLongestLiteralRun(RegexFixedWidthAtom[] atoms, out int offset, out byte[]? literal)
    {
        offset = 0;
        literal = null;
        int bestStart = 0;
        int bestLength = 0;
        int runStart = 0;
        var run = new List<byte>();
        for (int index = 0; index < atoms.Length; index++)
        {
            if (atoms[index].TryGetLiteral(out byte value))
            {
                if (run.Count == 0)
                {
                    runStart = index;
                }

                run.Add(value);
                continue;
            }

            if (run.Count > bestLength)
            {
                bestStart = runStart;
                bestLength = run.Count;
                literal = run.ToArray();
            }

            run.Clear();
        }

        if (run.Count > bestLength)
        {
            bestStart = runStart;
            bestLength = run.Count;
            literal = run.ToArray();
        }

        offset = bestStart;
        return bestLength > 0 && literal is not null;
    }

    private static bool TryChooseAnchor(
        RegexFixedWidthAtom[][] alternatives,
        int width,
        out int anchorIndex,
        out bool[]? anchorBytes,
        out byte[]? needles)
    {
        anchorIndex = 0;
        anchorBytes = null;
        needles = null;
        int bestCount = int.MaxValue;
        for (int index = 0; index < width; index++)
        {
            bool[] bytes = new bool[256];
            int count = 0;
            for (int alternativeIndex = 0; alternativeIndex < alternatives.Length; alternativeIndex++)
            {
                RegexFixedWidthAtom atom = alternatives[alternativeIndex][index];
                for (int value = 0; value <= byte.MaxValue; value++)
                {
                    if (!bytes[value] && atom.Matches((byte)value))
                    {
                        bytes[value] = true;
                        count++;
                    }
                }
            }

            if (count > 0 && count < bestCount)
            {
                bestCount = count;
                anchorIndex = index;
                anchorBytes = bytes;
                if (count == 1)
                {
                    break;
                }
            }
        }

        if (anchorBytes is null || bestCount == 0 || bestCount == 256)
        {
            return false;
        }

        needles = BuildNeedles(anchorBytes, bestCount);
        return true;
    }

    private static byte[] BuildNeedles(bool[] bytes, int count)
    {
        byte[] needles = new byte[count];
        int write = 0;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (bytes[value])
            {
                needles[write++] = (byte)value;
            }
        }

        return needles;
    }

    private static bool TryAppendAtoms(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<RegexFixedWidthAtom> atoms)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        switch (node)
        {
            case RegexSequenceNode sequence:
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (!TryAppendAtoms(sequence.Nodes[index], options, atoms))
                    {
                        return false;
                    }
                }

                return true;

            case RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom:
                ReadOnlySpan<byte> literal = atom.Value.Span;
                if (literal.IsEmpty)
                {
                    return false;
                }

                for (int index = 0; index < literal.Length; index++)
                {
                    atoms.Add(RegexFixedWidthAtom.CreateLiteral(literal[index]));
                }

                return true;

            case RegexAtomNode atom:
                if (IsPredicate(atom.Kind) ||
                    RegexByteClass.RequiresUtf8ScalarMatch(
                        atom.Kind,
                        atom.Value.Span,
                        options.Utf8,
                        options.CaseInsensitive,
                        options.UnicodeClasses))
                {
                    return false;
                }

                atoms.Add(RegexFixedWidthAtom.CreateLookup(atom, options));
                return true;

            default:
                return false;
        }
    }

    private static bool IsPredicate(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.StartAnchor
            or RegexSyntaxKind.EndAnchor
            or RegexSyntaxKind.AbsoluteStartAnchor
            or RegexSyntaxKind.AbsoluteEndAnchor
            or RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary
            or RegexSyntaxKind.WordStartBoundary
            or RegexSyntaxKind.WordEndBoundary
            or RegexSyntaxKind.WordStartHalfBoundary
            or RegexSyntaxKind.WordEndHalfBoundary;
    }

    private static RegexSyntaxNode UnwrapTransparentNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }
}
