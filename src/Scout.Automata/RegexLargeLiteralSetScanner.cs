using System.Buffers.Binary;

namespace Scout;

internal sealed class RegexLargeLiteralSetScanner
{
    private const int BlockLength = 2;
    private const int TripleBlockLength = 3;
    private const int PreferredWideBlockLength = 6;
    private const int FallbackWideBlockLength = 4;
    private const int MinimumLiteralCount = 128;
    private const int TripleShiftMinimumLiteralCount = 64 * 1024;
    private const int TripleShiftTableLength = 1 << 24;
    private const int SingletonTrieObservedByteThreshold = 4096;

    private readonly byte[][] literals;
    private readonly int[] singleByteLiteralIds;
    private readonly int[] shifts;
    private readonly bool useTripleShifts;
    private readonly int[]?[] buckets;
    private readonly Dictionary<ulong, int[]>? wideBuckets;
    private readonly int minLiteralLength;
    private readonly int suffixOffset;
    private readonly int tripleSuffixOffset;
    private readonly int wideSuffixBacktrack;
    private readonly int tripleWideSuffixBacktrack;
    private readonly object tripleShiftInitializationLock = new();
    private readonly object singletonTrieInitializationLock = new();
    private byte[]? tripleShifts;
    private volatile bool tripleShiftsInitialized;
    private RegexLargeLiteralTrieScanner? singletonTrieScanner;
    private int singletonTrieObservedBytes;
    private volatile bool singletonTrieInitialized;

    private RegexLargeLiteralSetScanner(
        byte[][] literals,
        int[] singleByteLiteralIds,
        int[] shifts,
        bool useTripleShifts,
        int[]?[] buckets,
        Dictionary<ulong, int[]>? wideBuckets,
        int minLiteralLength,
        int wideBlockLength)
    {
        this.literals = literals;
        this.singleByteLiteralIds = singleByteLiteralIds;
        this.shifts = shifts;
        this.useTripleShifts = useTripleShifts;
        this.buckets = buckets;
        this.wideBuckets = wideBuckets;
        this.minLiteralLength = minLiteralLength;
        suffixOffset = minLiteralLength - BlockLength;
        tripleSuffixOffset = minLiteralLength - TripleBlockLength;
        wideSuffixBacktrack = Math.Max(0, wideBlockLength - BlockLength);
        tripleWideSuffixBacktrack = Math.Max(0, wideBlockLength - TripleBlockLength);
    }

    public static bool TryCreate(IReadOnlyList<byte[]> literals, out RegexLargeLiteralSetScanner? scanner)
    {
        scanner = null;
        if (literals.Count < MinimumLiteralCount)
        {
            return false;
        }

        int minLength = int.MaxValue;
        int longLiteralCount = 0;
        byte[][] ownedLiterals = new byte[literals.Count][];
        int[]? singleByteLiteralIds = null;
        for (int index = 0; index < literals.Count; index++)
        {
            byte[] literal = literals[index];
            if (literal.Length == 0)
            {
                return false;
            }

            ownedLiterals[index] = literal.ToArray();
            if (literal.Length == 1)
            {
                singleByteLiteralIds ??= CreateSingleByteLiteralIds();
                ref int literalId = ref singleByteLiteralIds[literal[0]];
                if (literalId < 0 || index < literalId)
                {
                    literalId = index;
                }

                continue;
            }

            longLiteralCount++;
            minLength = Math.Min(minLength, literal.Length);
        }

        if (longLiteralCount == 0)
        {
            return false;
        }

        int maxShift = minLength - BlockLength + 1;
        int[] shifts = new int[ushort.MaxValue + 1];
        Array.Fill(shifts, maxShift);
        var bucketLists = new List<int>[ushort.MaxValue + 1];
        int suffixOffset = minLength - BlockLength;
        for (int index = 0; index < ownedLiterals.Length; index++)
        {
            byte[] literal = ownedLiterals[index];
            if (literal.Length < BlockLength)
            {
                continue;
            }

            for (int offset = 0; offset <= suffixOffset; offset++)
            {
                ushort key = BlockKey(literal.AsSpan(offset));
                shifts[key] = Math.Min(shifts[key], suffixOffset - offset);
            }

            ushort suffixKey = BlockKey(literal.AsSpan(suffixOffset));
            (bucketLists[suffixKey] ??= []).Add(index);
        }

        int[][] buckets = new int[ushort.MaxValue + 1][];
        for (int index = 0; index < bucketLists.Length; index++)
        {
            buckets[index] = bucketLists[index]?.ToArray() ?? [];
        }

        Dictionary<ulong, int[]>? wideBuckets = BuildWideSuffixBuckets(
            ownedLiterals,
            minLength,
            PreferredWideBlockLength,
            out int wideBlockLength);
        wideBuckets ??= BuildWideSuffixBuckets(
            ownedLiterals,
            minLength,
            FallbackWideBlockLength,
            out wideBlockLength);
        bool useTripleShifts = wideBuckets is not null && ShouldUseTripleShifts(minLength, longLiteralCount);
        scanner = new RegexLargeLiteralSetScanner(
            ownedLiterals,
            singleByteLiteralIds ?? [],
            shifts,
            useTripleShifts,
            buckets,
            wideBuckets,
            minLength,
            wideBlockLength);
        return true;
    }

    public RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (GetSingletonTrieScanner(haystack) is RegexLargeLiteralTrieScanner singletonTrie)
        {
            return singletonTrie.Find(haystack, startAt);
        }

        int start = Math.Clamp(startAt, 0, haystack.Length);
        RegexLiteralSetCandidate? longCandidate = FindLongLiteral(haystack, start);
        if (singleByteLiteralIds.Length == 0)
        {
            return longCandidate;
        }

        int singleEnd = longCandidate.HasValue
            ? longCandidate.Value.Match.Start
            : haystack.Length;
        if (TryFindSingleByteLiteral(haystack, start, singleEnd, out RegexLiteralSetCandidate singleCandidate))
        {
            return singleCandidate;
        }

        if (!longCandidate.HasValue)
        {
            return null;
        }

        RegexLiteralSetCandidate candidate = longCandidate.Value;
        int sameStartSingleId = singleByteLiteralIds[haystack[candidate.Match.Start]];
        return sameStartSingleId >= 0 && sameStartSingleId < candidate.LiteralId
            ? new RegexLiteralSetCandidate(sameStartSingleId, new RegexMatch(candidate.Match.Start, 1))
            : candidate;
    }

    private RegexLiteralSetCandidate? FindLongLiteral(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (useTripleShifts)
        {
            return FindLongLiteralTripleShift(haystack, startAt, GetTripleShifts());
        }

        int start = Math.Clamp(startAt, 0, haystack.Length);
        int position = start + suffixOffset;
        while (position <= haystack.Length - BlockLength)
        {
            ushort key = BlockKey(haystack[position..]);
            int shift = shifts[key];
            if (shift != 0)
            {
                position += shift;
                continue;
            }

            int candidateStart = position - suffixOffset;
            int[] literalIds;
            if (wideBuckets is not null)
            {
                ulong wideKey = WideBlockKey(haystack[(position - wideSuffixBacktrack)..], wideSuffixBacktrack + BlockLength);
                if (!wideBuckets.TryGetValue(wideKey, out literalIds!))
                {
                    position++;
                    continue;
                }
            }
            else
            {
                literalIds = buckets[key]!;
            }

            for (int index = 0; index < literalIds.Length; index++)
            {
                int literalId = literalIds[index];
                byte[] literal = literals[literalId];
                if (literal.Length <= haystack.Length - candidateStart &&
                    haystack.Slice(candidateStart, literal.Length).SequenceEqual(literal))
                {
                    return new RegexLiteralSetCandidate(literalId, new RegexMatch(candidateStart, literal.Length));
                }
            }

            position++;
        }

        return null;
    }

    private RegexLiteralSetCandidate? FindLongLiteralTripleShift(ReadOnlySpan<byte> haystack, int startAt, byte[] tripleShifts)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        int position = start + tripleSuffixOffset;
        while (position <= haystack.Length - TripleBlockLength)
        {
            int shift = tripleShifts[TripleBlockKey(haystack[position..])];
            if (shift != 0)
            {
                position += shift;
                continue;
            }

            int candidateStart = position - tripleSuffixOffset;
            ulong wideKey = WideBlockKey(
                haystack[(position - tripleWideSuffixBacktrack)..],
                tripleWideSuffixBacktrack + TripleBlockLength);
            if (!wideBuckets!.TryGetValue(wideKey, out int[]? literalIds))
            {
                position++;
                continue;
            }

            for (int index = 0; index < literalIds.Length; index++)
            {
                int literalId = literalIds[index];
                byte[] literal = literals[literalId];
                if (literal.Length <= haystack.Length - candidateStart &&
                    haystack.Slice(candidateStart, literal.Length).SequenceEqual(literal))
                {
                    return new RegexLiteralSetCandidate(literalId, new RegexMatch(candidateStart, literal.Length));
                }
            }

            position++;
        }

        return null;
    }

    private byte[] GetTripleShifts()
    {
        if (!tripleShiftsInitialized)
        {
            lock (tripleShiftInitializationLock)
            {
                if (!tripleShiftsInitialized)
                {
                    tripleShifts = BuildTripleShifts(literals, minLiteralLength);
                    tripleShiftsInitialized = true;
                }
            }
        }

        return tripleShifts!;
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
        if (GetSingletonTrieScanner(haystack) is RegexLargeLiteralTrieScanner singletonTrie)
        {
            return singletonTrie.CountOrSum(haystack, startAt, sumSpans);
        }

        long total = 0;
        int position = Math.Clamp(startAt, 0, haystack.Length);
        RegexLiteralSetCandidate? longCandidate = null;
        while (position <= haystack.Length)
        {
            if (singleByteLiteralIds.Length == 0)
            {
                RegexLiteralSetCandidate? nextLong = FindLongLiteral(haystack, position);
                if (!nextLong.HasValue)
                {
                    return total;
                }

                RegexMatch nextLongMatch = nextLong.Value.Match;
                total += sumSpans ? nextLongMatch.Length : 1;
                position = nextLongMatch.End;
                continue;
            }

            if (!longCandidate.HasValue || longCandidate.Value.Match.Start < position)
            {
                longCandidate = FindLongLiteral(haystack, position);
            }

            int singleEnd = longCandidate.HasValue
                ? longCandidate.Value.Match.Start
                : haystack.Length;
            if (TryFindSingleByteLiteral(haystack, position, singleEnd, out RegexLiteralSetCandidate singleCandidate))
            {
                total += sumSpans ? singleCandidate.Match.Length : 1;
                position = singleCandidate.Match.End;
                continue;
            }

            if (!longCandidate.HasValue)
            {
                return total;
            }

            RegexLiteralSetCandidate candidate = longCandidate.Value;
            int sameStartSingleId = singleByteLiteralIds[haystack[candidate.Match.Start]];
            if (sameStartSingleId >= 0 && sameStartSingleId < candidate.LiteralId)
            {
                candidate = new RegexLiteralSetCandidate(sameStartSingleId, new RegexMatch(candidate.Match.Start, 1));
            }

            RegexMatch match = candidate.Match;
            total += sumSpans ? match.Length : 1;
            position = match.End;
            longCandidate = null;
        }

        return total;
    }

    private RegexLargeLiteralTrieScanner? GetSingletonTrieScanner(ReadOnlySpan<byte> haystack)
    {
        if (singleByteLiteralIds.Length == 0)
        {
            return null;
        }

        if (!singletonTrieInitialized)
        {
            lock (singletonTrieInitializationLock)
            {
                if (!singletonTrieInitialized)
                {
                    singletonTrieObservedBytes = Math.Min(
                        SingletonTrieObservedByteThreshold,
                        singletonTrieObservedBytes + haystack.Length);
                    if (singletonTrieObservedBytes < SingletonTrieObservedByteThreshold)
                    {
                        return null;
                    }

                    RegexLargeLiteralTrieScanner.TryCreate(literals, out singletonTrieScanner);
                    singletonTrieInitialized = true;
                }
            }
        }

        return singletonTrieScanner;
    }

    private bool TryFindSingleByteLiteral(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        out RegexLiteralSetCandidate candidate)
    {
        int searchEnd = Math.Min(end, haystack.Length);
        for (int index = start; index < searchEnd; index++)
        {
            int literalId = singleByteLiteralIds[haystack[index]];
            if (literalId >= 0)
            {
                candidate = new RegexLiteralSetCandidate(literalId, new RegexMatch(index, 1));
                return true;
            }
        }

        candidate = default;
        return false;
    }

    private static int[] CreateSingleByteLiteralIds()
    {
        int[] literalIds = new int[byte.MaxValue + 1];
        Array.Fill(literalIds, -1);
        return literalIds;
    }

    private static ushort BlockKey(ReadOnlySpan<byte> value)
    {
        return (ushort)(value[0] | (value[1] << 8));
    }

    private static int TripleBlockKey(ReadOnlySpan<byte> value)
    {
        return value[0] | (value[1] << 8) | (value[2] << 16);
    }

    private static bool ShouldUseTripleShifts(int minLength, int longLiteralCount)
    {
        if (longLiteralCount < TripleShiftMinimumLiteralCount ||
            minLength < TripleBlockLength)
        {
            return false;
        }

        int suffixOffset = minLength - TripleBlockLength;
        int maxShift = suffixOffset + 1;
        if (maxShift > byte.MaxValue)
        {
            return false;
        }

        return true;
    }

    private static byte[] BuildTripleShifts(byte[][] literals, int minLength)
    {
        int suffixOffset = minLength - TripleBlockLength;
        int maxShift = suffixOffset + 1;
        byte[] shifts = new byte[TripleShiftTableLength];
        Array.Fill(shifts, (byte)maxShift);
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            if (literal.Length < TripleBlockLength)
            {
                continue;
            }

            for (int offset = 0; offset <= suffixOffset; offset++)
            {
                int key = TripleBlockKey(literal.AsSpan(offset));
                byte shift = (byte)(suffixOffset - offset);
                if (shift < shifts[key])
                {
                    shifts[key] = shift;
                }
            }
        }

        return shifts;
    }

    private static Dictionary<ulong, int[]>? BuildWideSuffixBuckets(
        byte[][] literals,
        int minLength,
        int blockLength,
        out int actualBlockLength)
    {
        actualBlockLength = 0;
        if (minLength < blockLength)
        {
            return null;
        }

        var bucketLists = new Dictionary<ulong, List<int>>();
        int suffixOffset = minLength - blockLength;
        for (int index = 0; index < literals.Length; index++)
        {
            if (literals[index].Length < minLength)
            {
                continue;
            }

            ulong key = WideBlockKey(literals[index].AsSpan(suffixOffset), blockLength);
            if (!bucketLists.TryGetValue(key, out List<int>? bucket))
            {
                bucket = [];
                bucketLists.Add(key, bucket);
            }

            bucket.Add(index);
        }

        var buckets = new Dictionary<ulong, int[]>(bucketLists.Count);
        foreach (KeyValuePair<ulong, List<int>> bucket in bucketLists)
        {
            buckets.Add(bucket.Key, bucket.Value.ToArray());
        }

        actualBlockLength = blockLength;
        return buckets;
    }

    private static ulong WideBlockKey(ReadOnlySpan<byte> value, int length)
    {
        if (length == PreferredWideBlockLength)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(value) |
                ((ulong)BinaryPrimitives.ReadUInt16LittleEndian(value[4..]) << 32);
        }

        if (length == FallbackWideBlockLength)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(value);
        }

        ulong key = 0;
        for (int index = 0; index < length; index++)
        {
            key |= (ulong)value[index] << (index * 8);
        }

        return key;
    }
}
