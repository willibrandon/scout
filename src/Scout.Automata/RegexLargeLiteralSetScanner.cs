namespace Scout;

internal sealed class RegexLargeLiteralSetScanner
{
    private const int BlockLength = 2;
    private const int MinimumLiteralCount = 128;

    private readonly byte[][] literals;
    private readonly int[] shifts;
    private readonly int[]?[] buckets;
    private readonly int minLiteralLength;
    private readonly int suffixOffset;

    private RegexLargeLiteralSetScanner(byte[][] literals, int[] shifts, int[]?[] buckets, int minLiteralLength)
    {
        this.literals = literals;
        this.shifts = shifts;
        this.buckets = buckets;
        this.minLiteralLength = minLiteralLength;
        suffixOffset = minLiteralLength - BlockLength;
    }

    public static bool TryCreate(IReadOnlyList<byte[]> literals, out RegexLargeLiteralSetScanner? scanner)
    {
        scanner = null;
        if (literals.Count < MinimumLiteralCount)
        {
            return false;
        }

        int minLength = int.MaxValue;
        byte[][] ownedLiterals = new byte[literals.Count][];
        for (int index = 0; index < literals.Count; index++)
        {
            byte[] literal = literals[index];
            if (literal.Length < BlockLength)
            {
                return false;
            }

            ownedLiterals[index] = literal.ToArray();
            minLength = Math.Min(minLength, literal.Length);
        }

        int maxShift = minLength - BlockLength + 1;
        int[] shifts = new int[ushort.MaxValue + 1];
        Array.Fill(shifts, maxShift);
        var bucketLists = new List<int>[ushort.MaxValue + 1];
        int suffixOffset = minLength - BlockLength;
        for (int index = 0; index < ownedLiterals.Length; index++)
        {
            byte[] literal = ownedLiterals[index];
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

        scanner = new RegexLargeLiteralSetScanner(
            ownedLiterals,
            shifts,
            buckets,
            minLength);
        return true;
    }

    public RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
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
            int[] literalIds = buckets[key]!;
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
        int position = Math.Clamp(startAt, 0, haystack.Length);
        while (position <= haystack.Length)
        {
            RegexLiteralSetCandidate? candidate = Find(haystack, position);
            if (!candidate.HasValue)
            {
                return total;
            }

            RegexMatch match = candidate.Value.Match;
            total += sumSpans ? match.Length : 1;
            position = match.End;
        }

        return total;
    }

    private static ushort BlockKey(ReadOnlySpan<byte> value)
    {
        return (ushort)(value[0] | (value[1] << 8));
    }
}
