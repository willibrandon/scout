namespace Scout;

internal sealed class RegexFixedWidthAlternationEngine
{
    private const int MinAlternativeCount = 2;
    private const int MaxAlternativeCount = 32;
    private const int MaxWidth = 64;
    private const int MinLiteralSeedLength = 2;

    private readonly RegexFixedWidthAtom[][] alternatives;
    private readonly RegexFixedWidthLiteralSeed[]? literalSeeds;
    private readonly MemmemFinder[]? literalSeedFinders;
    private readonly RegexLiteralPrefixScanner? literalSeedScanner;
    private readonly RegexTeddyPrefilter? literalSeedTeddy;
    private readonly RegexFixedWidthExactSetMatcher? exactSetMatcher;
    private readonly bool[] anchorBytes;
    private readonly byte[] anchorNeedles;
    private readonly int width;
    private readonly int anchorIndex;
    private readonly int literalSeedOffset;
    private readonly bool anchoredAtStart;

    private RegexFixedWidthAlternationEngine(
        RegexFixedWidthAtom[][] alternatives,
        RegexFixedWidthLiteralSeed[]? literalSeeds,
        MemmemFinder[]? literalSeedFinders,
        RegexLiteralPrefixScanner? literalSeedScanner,
        RegexTeddyPrefilter? literalSeedTeddy,
        RegexFixedWidthExactSetMatcher? exactSetMatcher,
        bool[] anchorBytes,
        byte[] anchorNeedles,
        int width,
        int anchorIndex,
        int literalSeedOffset,
        bool anchoredAtStart)
    {
        this.alternatives = alternatives;
        this.literalSeeds = literalSeeds;
        this.literalSeedFinders = literalSeedFinders;
        this.literalSeedScanner = literalSeedScanner;
        this.literalSeedTeddy = literalSeedTeddy;
        this.exactSetMatcher = exactSetMatcher;
        this.anchorBytes = anchorBytes;
        this.anchorNeedles = anchorNeedles;
        this.width = width;
        this.anchorIndex = anchorIndex;
        this.literalSeedOffset = literalSeedOffset;
        this.anchoredAtStart = anchoredAtStart;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        int captureCount,
        RegexCompileOptions options,
        out RegexFixedWidthAlternationEngine? engine)
    {
        engine = null;
        _ = captureCount;
        if (options.CaseInsensitive || options.Utf8)
        {
            return false;
        }

        if (!TryCollectAlternatives(root, options, out RegexFixedWidthAtom[][] compiledAlternatives, out bool anchoredAtStart) ||
            compiledAlternatives.Length > MaxAlternativeCount)
        {
            return false;
        }

        int width = -1;
        for (int index = 0; index < compiledAlternatives.Length; index++)
        {
            int alternativeWidth = compiledAlternatives[index].Length;
            if (alternativeWidth == 0 || alternativeWidth > MaxWidth)
            {
                return false;
            }

            if (width < 0)
            {
                width = alternativeWidth;
            }
            else if (alternativeWidth != width)
            {
                return false;
            }
        }

        if (width <= 0 || !TryChooseAnchor(compiledAlternatives, width, out int anchorIndex, out bool[]? anchorBytes, out byte[]? needles))
        {
            return false;
        }

        RegexFixedWidthLiteralSeed[]? literalSeeds = TryCreateLiteralSeeds(compiledAlternatives, out RegexFixedWidthLiteralSeed[]? seeds)
            ? seeds
            : null;
        if (compiledAlternatives.Length < MinAlternativeCount &&
            (literalSeeds is null || literalSeeds[0].Offset != 0))
        {
            return false;
        }

        TryCreateLiteralSeedAccelerators(
            literalSeeds,
            out MemmemFinder[]? literalSeedFinders,
            out RegexLiteralPrefixScanner? literalSeedScanner,
            out RegexTeddyPrefilter? literalSeedTeddy,
            out int literalSeedOffset);
        RegexFixedWidthExactSetMatcher.TryCreate(
            compiledAlternatives,
            width,
            out RegexFixedWidthExactSetMatcher? exactSetMatcher);
        engine = new RegexFixedWidthAlternationEngine(
            compiledAlternatives,
            literalSeeds,
            literalSeedFinders,
            literalSeedScanner,
            literalSeedTeddy,
            exactSetMatcher,
            anchorBytes!,
            needles!,
            width,
            anchorIndex,
            literalSeedOffset,
            anchoredAtStart);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (anchoredAtStart)
        {
            return start == 0 && TryMatchAt(haystack, 0)
                ? new RegexMatch(0, width)
                : null;
        }

        if (exactSetMatcher is not null &&
            exactSetMatcher.Width == 2 &&
            literalSeeds is null)
        {
            int candidate = exactSetMatcher.Find(haystack, start);
            return candidate < 0 ? null : new RegexMatch(candidate, width);
        }

        if (literalSeedTeddy is not null)
        {
            return FindWithLiteralSeedTeddy(haystack, start);
        }

        if (literalSeedFinders is not null)
        {
            return FindWithLiteralSeedFinders(haystack, start);
        }

        if (literalSeedScanner is not null)
        {
            return FindWithLiteralSeedScanner(haystack, start);
        }

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

    private RegexMatch? FindWithLiteralSeedFinders(ReadOnlySpan<byte> haystack, int minimumStart)
    {
        RegexFixedWidthLiteralSeed[] seeds = literalSeeds!;
        int bestStart = int.MaxValue;
        for (int index = 0; index < seeds.Length; index++)
        {
            if (TryFindSeedMatch(
                    haystack,
                    minimumStart,
                    seeds[index],
                    literalSeedFinders![index],
                    bestStart,
                    out int candidate) &&
                candidate < bestStart)
            {
                bestStart = candidate;
            }
        }

        return bestStart == int.MaxValue ? null : new RegexMatch(bestStart, width);
    }

    private RegexMatch? FindWithLiteralSeedTeddy(ReadOnlySpan<byte> haystack, int minimumStart)
    {
        int searchAt = Math.Min(haystack.Length, minimumStart + literalSeedOffset);
        for (int seedStart = literalSeedTeddy!.FindCandidate(haystack, searchAt);
             seedStart >= 0;
             seedStart = literalSeedTeddy.FindCandidate(haystack, seedStart + 1))
        {
            int candidate = seedStart - literalSeedOffset;
            if (candidate >= minimumStart && TryMatchAt(haystack, candidate))
            {
                return new RegexMatch(candidate, width);
            }
        }

        return null;
    }

    private RegexMatch? FindWithLiteralSeedScanner(ReadOnlySpan<byte> haystack, int minimumStart)
    {
        int searchAt = Math.Min(haystack.Length, minimumStart + literalSeedOffset);
        for (int seedStart = literalSeedScanner!.FindCandidate(haystack, searchAt);
             seedStart >= 0;
             seedStart = literalSeedScanner.FindCandidate(haystack, seedStart + 1))
        {
            int candidate = seedStart - literalSeedOffset;
            if (candidate >= minimumStart && TryMatchAt(haystack, candidate))
            {
                return new RegexMatch(candidate, width);
            }
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (anchoredAtStart && start != 0)
        {
            return null;
        }

        return TryMatchAt(haystack, start)
            ? new RegexMatch(start, width)
            : null;
    }

    public bool IsMatch(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (anchoredAtStart)
        {
            return start == 0 && TryMatchAt(haystack, 0);
        }

        if (exactSetMatcher is not null &&
            exactSetMatcher.Width == 2 &&
            literalSeeds is null)
        {
            return exactSetMatcher.Find(haystack, start) >= 0;
        }

        if (literalSeedTeddy is not null)
        {
            return IsMatchWithLiteralSeedTeddy(haystack, start);
        }

        if (literalSeedFinders is not null)
        {
            return FindWithLiteralSeedFinders(haystack, start).HasValue;
        }

        if (literalSeedScanner is not null)
        {
            return IsMatchWithLiteralSeedScanner(haystack, start);
        }

        if (literalSeeds is not null)
        {
            return FindWithLiteralSeeds(haystack, start).HasValue;
        }

        while (TryFindAnchor(haystack, start, out int candidate))
        {
            if (TryMatchAt(haystack, candidate))
            {
                return true;
            }

            start = candidate + 1;
        }

        return false;
    }

    private bool IsMatchWithLiteralSeedTeddy(ReadOnlySpan<byte> haystack, int minimumStart)
    {
        int searchAt = Math.Min(haystack.Length, minimumStart + literalSeedOffset);
        for (int seedStart = literalSeedTeddy!.FindCandidate(haystack, searchAt);
             seedStart >= 0;
             seedStart = literalSeedTeddy.FindCandidate(haystack, seedStart + 1))
        {
            int candidate = seedStart - literalSeedOffset;
            if (candidate >= minimumStart && TryMatchAt(haystack, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsMatchWithLiteralSeedScanner(ReadOnlySpan<byte> haystack, int minimumStart)
    {
        int searchAt = Math.Min(haystack.Length, minimumStart + literalSeedOffset);
        for (int seedStart = literalSeedScanner!.FindCandidate(haystack, searchAt);
             seedStart >= 0;
             seedStart = literalSeedScanner.FindCandidate(haystack, seedStart + 1))
        {
            int candidate = seedStart - literalSeedOffset;
            if (candidate >= minimumStart && TryMatchAt(haystack, candidate))
            {
                return true;
            }
        }

        return false;
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
        if (anchoredAtStart)
        {
            int start = Math.Clamp(startAt, 0, haystack.Length);
            return start == 0 && TryMatchAt(haystack, 0)
                ? (sumSpans ? width : 1)
                : 0;
        }

        if (exactSetMatcher is not null &&
            exactSetMatcher.Width == 2 &&
            literalSeeds is null)
        {
            return CountOrSumWithExactSet(haystack, startAt, sumSpans);
        }

        if (literalSeedFinders is not null ||
            literalSeeds is not null && literalSeedScanner is null && literalSeedTeddy is null)
        {
            return CountOrSumWithLiteralSeeds(haystack, startAt, sumSpans);
        }

        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (Find(haystack, offset) is RegexMatch match)
        {
            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return total;
    }

    private long CountOrSumWithLiteralSeeds(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        RegexFixedWidthLiteralSeed[] seeds = literalSeeds!;
        Span<int> nextStarts = stackalloc int[seeds.Length];
        nextStarts.Fill(-1);
        long total = 0;
        int nextAllowedStart = Math.Clamp(startAt, 0, haystack.Length);
        while (true)
        {
            int bestSeed = -1;
            int bestStart = int.MaxValue;
            for (int index = 0; index < seeds.Length; index++)
            {
                int candidate = nextStarts[index];
                if (candidate == int.MaxValue)
                {
                    continue;
                }

                if (candidate < nextAllowedStart)
                {
                    bool found = literalSeedFinders is not null
                        ? TryFindSeedMatch(
                            haystack,
                            nextAllowedStart,
                            seeds[index],
                            literalSeedFinders[index],
                            int.MaxValue,
                            out candidate)
                        : TryFindSeedMatch(haystack, nextAllowedStart, seeds[index], int.MaxValue, out candidate);
                    if (!found)
                    {
                        nextStarts[index] = int.MaxValue;
                        continue;
                    }

                    nextStarts[index] = candidate;
                }

                if (candidate < bestStart)
                {
                    bestSeed = index;
                    bestStart = candidate;
                }
            }

            if (bestSeed < 0)
            {
                return total;
            }

            total += sumSpans ? width : 1;
            nextAllowedStart = bestStart + width;
            nextStarts[bestSeed] = -1;
        }
    }

    private long CountOrSumWithExactSet(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int nextAllowedStart = Math.Clamp(startAt, 0, haystack.Length);
        while (true)
        {
            int candidate = exactSetMatcher!.Find(haystack, nextAllowedStart);
            if (candidate < 0)
            {
                return total;
            }

            total += sumSpans ? width : 1;
            nextAllowedStart = candidate + width;
        }
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
            int searchEnd = GetSeedSearchEnd(haystack.Length, seed, lastSeedStart, bestStart);
            if (searchAt >= searchEnd)
            {
                return false;
            }

            int offset = haystack[searchAt..searchEnd].IndexOf(seed.Literal);
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

    private bool TryFindSeedMatch(
        ReadOnlySpan<byte> haystack,
        int minimumStart,
        RegexFixedWidthLiteralSeed seed,
        MemmemFinder finder,
        int bestStart,
        out int candidate)
    {
        candidate = 0;
        int searchAt = minimumStart + seed.Offset;
        int lastSeedStart = haystack.Length - (width - seed.Offset);
        while (searchAt <= lastSeedStart && searchAt - seed.Offset < bestStart)
        {
            int searchEnd = GetSeedSearchEnd(haystack.Length, seed, lastSeedStart, bestStart);
            if (searchAt >= searchEnd)
            {
                return false;
            }

            int offset = finder.Find(haystack[searchAt..searchEnd]);
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

    private static int GetSeedSearchEnd(
        int haystackLength,
        RegexFixedWidthLiteralSeed seed,
        int lastSeedStart,
        int bestStart)
    {
        int maxSeedStart = lastSeedStart;
        if (bestStart != int.MaxValue)
        {
            int bestSeedStart = bestStart > int.MaxValue - seed.Offset
                ? int.MaxValue
                : bestStart + seed.Offset;
            maxSeedStart = Math.Min(maxSeedStart, bestSeedStart - 1);
        }

        return Math.Min(haystackLength, maxSeedStart + seed.Literal.Length);
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
        if (exactSetMatcher is not null)
        {
            return exactSetMatcher.Matches(haystack, start);
        }

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

    private static bool TryCreateLiteralSeedAccelerators(
        RegexFixedWidthLiteralSeed[]? seeds,
        out MemmemFinder[]? finders,
        out RegexLiteralPrefixScanner? scanner,
        out RegexTeddyPrefilter? teddy,
        out int offset)
    {
        finders = null;
        scanner = null;
        teddy = null;
        offset = 0;
        if (seeds is null || seeds.Length == 0)
        {
            return false;
        }

        offset = seeds[0].Offset;
        byte[][] literals = new byte[seeds.Length][];
        bool sameOffset = true;
        for (int index = 0; index < seeds.Length; index++)
        {
            if (seeds[index].Offset != offset)
            {
                sameOffset = false;
            }

            literals[index] = seeds[index].Literal;
        }

        if (sameOffset)
        {
            scanner = new RegexLiteralPrefixScanner(literals);
            if (LiteralsShareFirstByte(literals) &&
                RegexTeddyPrefilter.TryCreate(literals, out RegexTeddyPrefilter? createdTeddy))
            {
                teddy = createdTeddy;
            }
        }

        if (teddy is null && ShouldUseLiteralSeedFinders(literals))
        {
            finders = new MemmemFinder[literals.Length];
            for (int index = 0; index < literals.Length; index++)
            {
                finders[index] = new MemmemFinder(literals[index]);
            }
        }

        if (!sameOffset)
        {
            scanner = null;
            offset = 0;
        }

        return true;
    }

    private static bool ShouldUseLiteralSeedFinders(byte[][] literals)
    {
        if (literals.Length > 8)
        {
            return false;
        }

        int minLength = int.MaxValue;
        for (int index = 0; index < literals.Length; index++)
        {
            minLength = Math.Min(minLength, literals[index].Length);
        }

        return minLength >= MinLiteralSeedLength;
    }

    private static bool LiteralsShareFirstByte(byte[][] literals)
    {
        if (literals.Length == 0 || literals[0].Length == 0)
        {
            return false;
        }

        byte first = literals[0][0];
        for (int index = 1; index < literals.Length; index++)
        {
            if (literals[index].Length == 0 || literals[index][0] != first)
            {
                return false;
            }
        }

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

    private static bool TryCollectAlternatives(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexFixedWidthAtom[][] alternatives,
        out bool anchoredAtStart)
    {
        alternatives = [];
        anchoredAtStart = false;
        var expanded = new List<List<RegexFixedWidthAtom>> { new List<RegexFixedWidthAtom>() };
        if (!TryAppendRootAtoms(node, options, expanded, out anchoredAtStart))
        {
            return false;
        }

        alternatives = new RegexFixedWidthAtom[expanded.Count][];
        for (int index = 0; index < expanded.Count; index++)
        {
            if (expanded[index].Count == 0 || expanded[index].Count > MaxWidth)
            {
                alternatives = [];
                return false;
            }

            alternatives[index] = [.. expanded[index]];
        }

        return true;
    }

    private static bool TryAppendRootAtoms(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<List<RegexFixedWidthAtom>> alternatives,
        out bool anchoredAtStart)
    {
        anchoredAtStart = false;
        if (node is not RegexSequenceNode sequence ||
            sequence.Nodes.Count < 2 ||
            sequence.Nodes[0] is not RegexAtomNode anchor ||
            !IsStartAnchor(anchor.Kind, options))
        {
            return TryAppendAtoms(node, options, alternatives);
        }

        anchoredAtStart = true;
        return TryAppendSequenceAtoms(sequence, options, alternatives, startIndex: 1);
    }

    private static bool IsStartAnchor(RegexSyntaxKind kind, RegexCompileOptions options)
    {
        return kind is RegexSyntaxKind.AbsoluteStartAnchor ||
            kind is RegexSyntaxKind.StartAnchor && !options.MultiLine;
    }

    private static bool TryAppendAtoms(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<List<RegexFixedWidthAtom>> alternatives)
    {
        switch (node)
        {
            case RegexSequenceNode sequence:
                return TryAppendSequenceAtoms(sequence, options, alternatives, startIndex: 0);

            case RegexAlternationNode alternation:
                return TryAppendAlternationAtoms(alternation, options, alternatives);

            case RegexGroupNode group:
                RegexCompileOptions groupOptions = options.Apply(group.EnabledFlags, group.DisabledFlags);
                return !groupOptions.CaseInsensitive &&
                    !groupOptions.Utf8 &&
                    TryAppendAtoms(group.Child, groupOptions, alternatives);

            case RegexRepetitionNode repetition:
                return TryAppendRepetitionAtoms(repetition, options, alternatives);

            case RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom:
                ReadOnlySpan<byte> literal = atom.Value.Span;
                if (literal.Length != 1)
                {
                    return false;
                }

                return TryAppendAtomToAll(alternatives, RegexFixedWidthAtom.CreateLiteral(literal[0]));

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

                return TryAppendAtomToAll(alternatives, RegexFixedWidthAtom.CreateLookup(atom, options));

            default:
                return false;
        }
    }

    private static bool TryAppendSequenceAtoms(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        List<List<RegexFixedWidthAtom>> alternatives,
        int startIndex)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = startIndex; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                if (currentOptions.CaseInsensitive || currentOptions.Utf8)
                {
                    return false;
                }

                continue;
            }

            if (!TryAppendAtoms(child, currentOptions, alternatives))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendAlternationAtoms(
        RegexAlternationNode alternation,
        RegexCompileOptions options,
        List<List<RegexFixedWidthAtom>> alternatives)
    {
        var expanded = new List<List<RegexFixedWidthAtom>>();
        for (int existingIndex = 0; existingIndex < alternatives.Count; existingIndex++)
        {
            List<RegexFixedWidthAtom> existing = alternatives[existingIndex];
            for (int branchIndex = 0; branchIndex < alternation.Alternatives.Count; branchIndex++)
            {
                var branchAlternatives = new List<List<RegexFixedWidthAtom>> { new List<RegexFixedWidthAtom>(existing) };
                if (!TryAppendAtoms(alternation.Alternatives[branchIndex], options, branchAlternatives))
                {
                    return false;
                }

                for (int index = 0; index < branchAlternatives.Count; index++)
                {
                    expanded.Add(branchAlternatives[index]);
                    if (expanded.Count > MaxAlternativeCount)
                    {
                        return false;
                    }
                }
            }
        }

        alternatives.Clear();
        alternatives.AddRange(expanded);
        return alternatives.Count > 0;
    }

    private static bool TryAppendRepetitionAtoms(
        RegexRepetitionNode repetition,
        RegexCompileOptions options,
        List<List<RegexFixedWidthAtom>> alternatives)
    {
        if (repetition.Maximum != repetition.Minimum || repetition.Minimum < 0)
        {
            return false;
        }

        for (int count = 0; count < repetition.Minimum; count++)
        {
            if (!TryAppendAtoms(repetition.Child, options, alternatives))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendAtomToAll(
        List<List<RegexFixedWidthAtom>> alternatives,
        RegexFixedWidthAtom atom)
    {
        for (int index = 0; index < alternatives.Count; index++)
        {
            if (alternatives[index].Count >= MaxWidth)
            {
                return false;
            }

            alternatives[index].Add(atom);
        }

        return true;
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

}
