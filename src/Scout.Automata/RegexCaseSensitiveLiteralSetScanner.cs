using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexCaseSensitiveLiteralSetScanner
{
    private const int BlockLength = 2;
    private const int MaxLiteralCount = 8;
    private const int StartAnchorBonus = 80;

    private readonly Dictionary<ushort, RegexCaseSensitiveLiteralSetEntry[]> entriesByBlock;
    private readonly byte[] blockFirstBytes;
    private readonly byte[] blockSecondBytes;
    private readonly Vector128<byte>[] blockFirstVectors128;
    private readonly Vector128<byte>[] blockSecondVectors128;
    private readonly Vector256<byte>[] blockFirstVectors256;
    private readonly Vector256<byte>[] blockSecondVectors256;
    private readonly int maxAnchorIndex;

    private RegexCaseSensitiveLiteralSetScanner(IReadOnlyList<byte[]> literals)
    {
        Dictionary<ushort, int> blockFrequencies = BuildBlockFrequencies(literals);
        int[] byteFrequencies = BuildByteFrequencies(literals);
        var buckets = new Dictionary<ushort, List<RegexCaseSensitiveLiteralSetEntry>>();
        var distinctBlocks = new List<ushort>();
        for (int index = 0; index < literals.Count; index++)
        {
            byte[] literal = literals[index].ToArray();
            int anchorIndex = SelectAnchorIndex(literal, blockFrequencies, byteFrequencies);
            maxAnchorIndex = Math.Max(maxAnchorIndex, anchorIndex);

            ushort block = BlockKey(literal.AsSpan(anchorIndex));
            if (!buckets.TryGetValue(block, out List<RegexCaseSensitiveLiteralSetEntry>? entries))
            {
                entries = [];
                buckets.Add(block, entries);
                distinctBlocks.Add(block);
            }

            entries.Add(new RegexCaseSensitiveLiteralSetEntry(index, literal, anchorIndex));
        }

        entriesByBlock = new Dictionary<ushort, RegexCaseSensitiveLiteralSetEntry[]>(buckets.Count);
        foreach (KeyValuePair<ushort, List<RegexCaseSensitiveLiteralSetEntry>> bucket in buckets)
        {
            entriesByBlock.Add(bucket.Key, bucket.Value.ToArray());
        }

        blockFirstBytes = new byte[distinctBlocks.Count];
        blockSecondBytes = new byte[distinctBlocks.Count];
        blockFirstVectors128 = new Vector128<byte>[distinctBlocks.Count];
        blockSecondVectors128 = new Vector128<byte>[distinctBlocks.Count];
        blockFirstVectors256 = new Vector256<byte>[distinctBlocks.Count];
        blockSecondVectors256 = new Vector256<byte>[distinctBlocks.Count];
        for (int index = 0; index < distinctBlocks.Count; index++)
        {
            ushort block = distinctBlocks[index];
            byte first = (byte)block;
            byte second = (byte)(block >> 8);
            blockFirstBytes[index] = first;
            blockSecondBytes[index] = second;
            blockFirstVectors128[index] = Vector128.Create(first);
            blockSecondVectors128[index] = Vector128.Create(second);
            blockFirstVectors256[index] = Vector256.Create(first);
            blockSecondVectors256[index] = Vector256.Create(second);
        }
    }

    public static bool TryCreate(IReadOnlyList<byte[]> literals, out RegexCaseSensitiveLiteralSetScanner? scanner)
    {
        return TryCreateWithMaxLiteralCount(literals, MaxLiteralCount, out scanner);
    }

    public static bool TryCreateWithMaxLiteralCount(
        IReadOnlyList<byte[]> literals,
        int maxLiteralCount,
        out RegexCaseSensitiveLiteralSetScanner? scanner)
    {
        scanner = null;
        if (literals.Count <= 1 || literals.Count > maxLiteralCount)
        {
            return false;
        }

        if (!ContainsAsciiOrTwoByteUtf8Only(literals))
        {
            return false;
        }

        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].Length < BlockLength)
            {
                return false;
            }
        }

        scanner = new RegexCaseSensitiveLiteralSetScanner(literals);
        return true;
    }

    private static bool ContainsAsciiOrTwoByteUtf8Only(IReadOnlyList<byte[]> literals)
    {
        for (int literalIndex = 0; literalIndex < literals.Count; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            int index = 0;
            while (index < literal.Length)
            {
                byte value = literal[index];
                if (value <= 0x7F)
                {
                    index++;
                    continue;
                }

                if (value is < 0xC2 or > 0xDF ||
                    index + 1 >= literal.Length ||
                    literal[index + 1] is < 0x80 or > 0xBF)
                {
                    return false;
                }

                index += 2;
            }
        }

        return true;
    }

    public RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = startOffset;
        RegexLiteralSetCandidate? best = null;
        while (searchAt <= haystack.Length - BlockLength)
        {
            int anchorPosition = FindBlock(haystack, searchAt);
            if (anchorPosition < 0)
            {
                break;
            }

            if (best.HasValue && anchorPosition - maxAnchorIndex > best.Value.Match.Start)
            {
                break;
            }

            RegexCaseSensitiveLiteralSetEntry[] entries = entriesByBlock[BlockKey(haystack[anchorPosition..])];
            for (int index = 0; index < entries.Length; index++)
            {
                RegexCaseSensitiveLiteralSetEntry entry = entries[index];
                int start = anchorPosition - entry.AnchorIndex;
                if (start < startOffset ||
                    entry.Literal.Length > haystack.Length - start ||
                    haystack[start + entry.Literal.Length - 1] != entry.Literal[^1] ||
                    !haystack.Slice(start, entry.Literal.Length).SequenceEqual(entry.Literal))
                {
                    continue;
                }

                var candidate = new RegexLiteralSetCandidate(
                    entry.LiteralId,
                    new RegexMatch(start, entry.Literal.Length));
                if (IsBetter(candidate, best))
                {
                    best = candidate;
                }
            }

            searchAt = anchorPosition + 1;
        }

        return best;
    }

    public long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        int nextAllowedStart = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = nextAllowedStart;
        long total = 0;
        RegexLiteralSetCandidate? best = null;
        while (searchAt <= haystack.Length - BlockLength)
        {
            int anchorPosition = FindBlock(haystack, searchAt);
            if (anchorPosition < 0)
            {
                break;
            }

            if (best.HasValue &&
                (anchorPosition >= best.Value.Match.End ||
                 anchorPosition - maxAnchorIndex > best.Value.Match.Start))
            {
                AddMatch(best.Value.Match, sumSpans, ref total, ref nextAllowedStart);
                best = null;
                if (anchorPosition < nextAllowedStart)
                {
                    searchAt = nextAllowedStart;
                    continue;
                }
            }

            RegexCaseSensitiveLiteralSetEntry[] entries = entriesByBlock[BlockKey(haystack[anchorPosition..])];
            for (int index = 0; index < entries.Length; index++)
            {
                RegexCaseSensitiveLiteralSetEntry entry = entries[index];
                int start = anchorPosition - entry.AnchorIndex;
                if (start < nextAllowedStart ||
                    entry.Literal.Length > haystack.Length - start ||
                    haystack[start + entry.Literal.Length - 1] != entry.Literal[^1] ||
                    !haystack.Slice(start, entry.Literal.Length).SequenceEqual(entry.Literal))
                {
                    continue;
                }

                var candidate = new RegexLiteralSetCandidate(
                    entry.LiteralId,
                    new RegexMatch(start, entry.Literal.Length));
                if (IsBetter(candidate, best))
                {
                    best = candidate;
                }
            }

            searchAt = anchorPosition + 1;
        }

        if (best.HasValue)
        {
            AddMatch(best.Value.Match, sumSpans, ref total, ref nextAllowedStart);
        }

        return total;
    }

    private int FindBlock(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (startAt > haystack.Length - BlockLength)
        {
            return -1;
        }

        if (Avx2.IsSupported && haystack.Length - startAt > Vector256<byte>.Count)
        {
            return FindBlockVector256(haystack, startAt);
        }

        if (Sse2.IsSupported && haystack.Length - startAt > Vector128<byte>.Count)
        {
            return FindBlockVector128(haystack, startAt);
        }

        return FindBlockScalar(haystack, startAt);
    }

    private int FindBlockVector256(ReadOnlySpan<byte> haystack, int startAt)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = haystack.Length - Vector256<byte>.Count - 1;
        while (offset <= vectorEnd)
        {
            var current = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1));
            Vector256<byte> matches = Vector256<byte>.Zero;
            for (int index = 0; index < blockFirstVectors256.Length; index++)
            {
                matches = Avx2.Or(
                    matches,
                    Avx2.And(
                        Avx2.CompareEqual(current, blockFirstVectors256[index]),
                        Avx2.CompareEqual(next, blockSecondVectors256[index])));
            }

            uint mask = matches.ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector256<byte>.Count;
        }

        return FindBlockScalar(haystack, offset);
    }

    private int FindBlockVector128(ReadOnlySpan<byte> haystack, int startAt)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = haystack.Length - Vector128<byte>.Count - 1;
        while (offset <= vectorEnd)
        {
            var current = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1));
            Vector128<byte> matches = Vector128<byte>.Zero;
            for (int index = 0; index < blockFirstVectors128.Length; index++)
            {
                matches = Sse2.Or(
                    matches,
                    Sse2.And(
                        Sse2.CompareEqual(current, blockFirstVectors128[index]),
                        Sse2.CompareEqual(next, blockSecondVectors128[index])));
            }

            uint mask = matches.ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return FindBlockScalar(haystack, offset);
    }

    private int FindBlockScalar(ReadOnlySpan<byte> haystack, int startAt)
    {
        for (int index = startAt; index <= haystack.Length - BlockLength; index++)
        {
            if (entriesByBlock.ContainsKey(BlockKey(haystack[index..])))
            {
                return index;
            }
        }

        return -1;
    }

    private static int SelectAnchorIndex(
        ReadOnlySpan<byte> literal,
        Dictionary<ushort, int> blockFrequencies,
        int[] byteFrequencies)
    {
        int bestIndex = 0;
        int bestScore = int.MinValue;
        for (int index = 0; index <= literal.Length - BlockLength; index++)
        {
            int score = BlockScore(literal, index, blockFrequencies, byteFrequencies);
            if (score > bestScore)
            {
                bestIndex = index;
                bestScore = score;
            }
        }

        return bestIndex;
    }

    private static int BlockScore(
        ReadOnlySpan<byte> literal,
        int index,
        Dictionary<ushort, int> blockFrequencies,
        int[] byteFrequencies)
    {
        byte first = literal[index];
        byte second = literal[index + 1];
        int score = AnchorScore(first) + AnchorScore(second);
        score -= blockFrequencies[BlockKey(literal[index..])] * 512;
        score -= byteFrequencies[first] * 8;
        score -= byteFrequencies[second] * 8;
        if (IsUtf8TwoByteScalar(first, second))
        {
            score += 2048;
        }

        if (index == 0)
        {
            score += StartAnchorBonus;
        }

        if (first == (byte)' ' || second == (byte)' ')
        {
            score -= 256;
        }

        return score;
    }

    private static int AnchorScore(byte value)
    {
        if (value is >= 0x80 and <= 0xBF)
        {
            return 260;
        }

        if (value >= 0xC2)
        {
            return 80;
        }

        if (value is >= (byte)'0' and <= (byte)'9')
        {
            return 180;
        }

        if (value is >= (byte)'A' and <= (byte)'Z')
        {
            return 220;
        }

        if (value is < (byte)'a' or > (byte)'z')
        {
            return value == (byte)' ' ? 10 : 220;
        }

        return value switch
        {
            (byte)'q' or (byte)'z' => 260,
            (byte)'x' or (byte)'j' => 250,
            (byte)'k' => 240,
            (byte)'v' => 230,
            (byte)'b' or (byte)'p' => 220,
            (byte)'g' => 210,
            (byte)'w' => 200,
            (byte)'y' => 190,
            (byte)'f' => 180,
            (byte)'m' => 170,
            (byte)'c' => 160,
            (byte)'u' => 150,
            (byte)'l' => 140,
            (byte)'d' => 130,
            (byte)'r' => 120,
            (byte)'h' => 110,
            (byte)'s' => 100,
            (byte)'n' => 90,
            (byte)'i' => 80,
            (byte)'o' => 70,
            (byte)'a' => 60,
            (byte)'t' => 50,
            (byte)'e' => 40,
            _ => 30,
        };
    }

    private static Dictionary<ushort, int> BuildBlockFrequencies(IReadOnlyList<byte[]> literals)
    {
        var frequencies = new Dictionary<ushort, int>();
        for (int literalIndex = 0; literalIndex < literals.Count; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            for (int index = 0; index <= literal.Length - BlockLength; index++)
            {
                ushort block = BlockKey(literal.AsSpan(index));
                frequencies.TryGetValue(block, out int count);
                frequencies[block] = count + 1;
            }
        }

        return frequencies;
    }

    private static int[] BuildByteFrequencies(IReadOnlyList<byte[]> literals)
    {
        int[] frequencies = new int[256];
        for (int literalIndex = 0; literalIndex < literals.Count; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            for (int index = 0; index < literal.Length; index++)
            {
                frequencies[literal[index]]++;
            }
        }

        return frequencies;
    }

    private static bool IsUtf8TwoByteScalar(byte first, byte second)
    {
        return first is >= 0xC2 and <= 0xDF &&
            second is >= 0x80 and <= 0xBF;
    }

    private static ushort BlockKey(ReadOnlySpan<byte> value)
    {
        return (ushort)(value[0] | (value[1] << 8));
    }

    private static bool IsBetter(RegexLiteralSetCandidate candidate, RegexLiteralSetCandidate? best)
    {
        if (!best.HasValue)
        {
            return true;
        }

        RegexLiteralSetCandidate current = best.Value;
        if (candidate.Match.Start != current.Match.Start)
        {
            return candidate.Match.Start < current.Match.Start;
        }

        return candidate.LiteralId < current.LiteralId;
    }

    private static void AddMatch(RegexMatch match, bool sumSpans, ref long total, ref int nextAllowedStart)
    {
        total += sumSpans ? match.Length : 1;
        nextAllowedStart = match.End;
    }
}
