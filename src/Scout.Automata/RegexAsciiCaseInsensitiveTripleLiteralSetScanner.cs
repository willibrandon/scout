using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexAsciiCaseInsensitiveTripleLiteralSetScanner
{
    private const int BlockLength = 3;
    private const int MinimumLiteralCount = 4;
    private const int MaximumLiteralCount = 8;
    private const int MaximumDistinctBlocks = 8;

    private readonly Dictionary<uint, RegexAsciiCaseInsensitiveLiteralSetEntry[]> entriesByBlock;
    private readonly RegexAsciiCaseInsensitiveLiteralSetEntry[][] blockEntriesByMaskBit;
    private readonly Vector512<byte>[] blockLowMasks512 = new Vector512<byte>[BlockLength];
    private readonly Vector512<byte>[] blockHighMasks512 = new Vector512<byte>[BlockLength];
    private readonly Vector256<byte>[] blockLowMasks256 = new Vector256<byte>[BlockLength];
    private readonly Vector256<byte>[] blockHighMasks256 = new Vector256<byte>[BlockLength];
    private readonly bool blockBytesAreAsciiLetters;
    private readonly int maxAnchorIndex;

    private RegexAsciiCaseInsensitiveTripleLiteralSetScanner(
        Dictionary<uint, RegexAsciiCaseInsensitiveLiteralSetEntry[]> entriesByBlock,
        RegexAsciiCaseInsensitiveLiteralSetEntry[][] blockEntriesByMaskBit,
        List<uint> distinctBlocks,
        int maxAnchorIndex)
    {
        this.entriesByBlock = entriesByBlock;
        this.blockEntriesByMaskBit = blockEntriesByMaskBit;
        this.maxAnchorIndex = maxAnchorIndex;
        blockBytesAreAsciiLetters = BlocksAreAsciiLetters(distinctBlocks);
        BuildBlockMasks(distinctBlocks);
    }

    public static bool TryCreate(
        IReadOnlyList<byte[]> literals,
        out RegexAsciiCaseInsensitiveTripleLiteralSetScanner? scanner)
    {
        scanner = null;
        if (!CanUse(literals))
        {
            return false;
        }

        byte[][] normalized = NormalizeAsciiCase(literals);
        Dictionary<uint, int> blockFrequencies = BuildBlockFrequencies(normalized);
        int[] byteFrequencies = BuildByteFrequencies(normalized);
        var buckets = new Dictionary<uint, List<RegexAsciiCaseInsensitiveLiteralSetEntry>>();
        var distinctBlocks = new List<uint>();
        int maxAnchorIndex = 0;
        for (int index = 0; index < normalized.Length; index++)
        {
            byte[] literal = normalized[index];
            int anchorIndex = SelectAnchorIndex(literal, blockFrequencies, byteFrequencies);
            maxAnchorIndex = Math.Max(maxAnchorIndex, anchorIndex);

            uint block = BlockKey(literal.AsSpan(anchorIndex));
            if (!buckets.TryGetValue(block, out List<RegexAsciiCaseInsensitiveLiteralSetEntry>? entries))
            {
                if (distinctBlocks.Count == MaximumDistinctBlocks)
                {
                    return false;
                }

                entries = [];
                buckets.Add(block, entries);
                distinctBlocks.Add(block);
            }

            entries.Add(new RegexAsciiCaseInsensitiveLiteralSetEntry(index, literal, anchorIndex));
        }

        var entriesByBlock = new Dictionary<uint, RegexAsciiCaseInsensitiveLiteralSetEntry[]>(buckets.Count);
        var blockEntriesByMaskBit = new RegexAsciiCaseInsensitiveLiteralSetEntry[distinctBlocks.Count][];
        foreach (KeyValuePair<uint, List<RegexAsciiCaseInsensitiveLiteralSetEntry>> bucket in buckets)
        {
            entriesByBlock.Add(bucket.Key, bucket.Value.ToArray());
        }

        for (int index = 0; index < distinctBlocks.Count; index++)
        {
            blockEntriesByMaskBit[index] = entriesByBlock[distinctBlocks[index]];
        }

        scanner = new RegexAsciiCaseInsensitiveTripleLiteralSetScanner(
            entriesByBlock,
            blockEntriesByMaskBit,
            distinctBlocks,
            maxAnchorIndex);
        return true;
    }

    public long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        int nextAllowedStart = Math.Clamp(startAt, 0, haystack.Length);
        if (Avx512BW.IsSupported &&
            haystack.Length - nextAllowedStart > Vector512<byte>.Count + BlockLength - 1)
        {
            return CountOrSumBlockMaskVector512(haystack, nextAllowedStart, sumSpans);
        }

        if (Avx2.IsSupported &&
            haystack.Length - nextAllowedStart > Vector256<byte>.Count + BlockLength - 1)
        {
            return CountOrSumBlockMaskVector256(haystack, nextAllowedStart, sumSpans);
        }

        return FinishCountOrSumBlockScalar(
            haystack,
            nextAllowedStart,
            sumSpans,
            total: 0,
            nextAllowedStart,
            best: null);
    }

    private long CountOrSumBlockMaskVector512(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startOffset;
        int vectorEnd = haystack.Length - Vector512<byte>.Count - (BlockLength - 1);
        long total = 0;
        int nextAllowedStart = startOffset;
        RegexLiteralSetCandidate? best = null;
        var asciiCaseMask = Vector512.Create((byte)0x20);
        while (offset <= vectorEnd)
        {
            var rawCurrent = Vector512.LoadUnsafe(ref reference, (nuint)offset);
            var rawNext = Vector512.LoadUnsafe(ref reference, (nuint)(offset + 1));
            var rawThird = Vector512.LoadUnsafe(ref reference, (nuint)(offset + 2));
            Vector512<byte> current = blockBytesAreAsciiLetters
                ? rawCurrent | asciiCaseMask
                : FoldAsciiVector512(rawCurrent);
            Vector512<byte> next = blockBytesAreAsciiLetters
                ? rawNext | asciiCaseMask
                : FoldAsciiVector512(rawNext);
            Vector512<byte> third = blockBytesAreAsciiLetters
                ? rawThird | asciiCaseMask
                : FoldAsciiVector512(rawThird);
            Vector512<byte> candidates =
                BlockMaskVector512(current, byteIndex: 0) &
                BlockMaskVector512(next, byteIndex: 1) &
                BlockMaskVector512(third, byteIndex: 2);
            if (Avx512BW.CompareEqual(candidates, Vector512<byte>.Zero).ExtractMostSignificantBits() != ulong.MaxValue)
            {
                for (int lane = 0; lane < 8; lane++)
                {
                    ProcessBlockCandidateChunk(
                        haystack,
                        offset + lane * 8,
                        candidates.AsUInt64().GetElement(lane),
                        sumSpans,
                        ref total,
                        ref nextAllowedStart,
                        ref best);
                }
            }

            offset += Vector512<byte>.Count;
        }

        return FinishCountOrSumBlockScalar(
            haystack,
            offset,
            sumSpans,
            total,
            nextAllowedStart,
            best);
    }

    private long CountOrSumBlockMaskVector256(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startOffset;
        int vectorEnd = haystack.Length - Vector256<byte>.Count - (BlockLength - 1);
        long total = 0;
        int nextAllowedStart = startOffset;
        RegexLiteralSetCandidate? best = null;
        var asciiCaseMask = Vector256.Create((byte)0x20);
        while (offset <= vectorEnd)
        {
            var rawCurrent = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            var rawNext = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1));
            var rawThird = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 2));
            Vector256<byte> current = blockBytesAreAsciiLetters
                ? Avx2.Or(rawCurrent, asciiCaseMask)
                : FoldAsciiVector256(rawCurrent);
            Vector256<byte> next = blockBytesAreAsciiLetters
                ? Avx2.Or(rawNext, asciiCaseMask)
                : FoldAsciiVector256(rawNext);
            Vector256<byte> third = blockBytesAreAsciiLetters
                ? Avx2.Or(rawThird, asciiCaseMask)
                : FoldAsciiVector256(rawThird);
            Vector256<byte> candidates = Avx2.And(
                Avx2.And(
                    BlockMaskVector256(current, byteIndex: 0),
                    BlockMaskVector256(next, byteIndex: 1)),
                BlockMaskVector256(third, byteIndex: 2));
            if (Avx2.CompareEqual(candidates, Vector256<byte>.Zero).ExtractMostSignificantBits() != uint.MaxValue)
            {
                for (int lane = 0; lane < 4; lane++)
                {
                    ProcessBlockCandidateChunk(
                        haystack,
                        offset + lane * 8,
                        candidates.AsUInt64().GetElement(lane),
                        sumSpans,
                        ref total,
                        ref nextAllowedStart,
                        ref best);
                }
            }

            offset += Vector256<byte>.Count;
        }

        return FinishCountOrSumBlockScalar(
            haystack,
            offset,
            sumSpans,
            total,
            nextAllowedStart,
            best);
    }

    private long FinishCountOrSumBlockScalar(
        ReadOnlySpan<byte> haystack,
        int searchAt,
        bool sumSpans,
        long total,
        int nextAllowedStart,
        RegexLiteralSetCandidate? best)
    {
        while (searchAt <= haystack.Length - BlockLength)
        {
            uint block = FoldedBlockKey(haystack, searchAt);
            if (entriesByBlock.TryGetValue(block, out RegexAsciiCaseInsensitiveLiteralSetEntry[]? entries))
            {
                ProcessBlockCandidate(
                    haystack,
                    searchAt,
                    entries,
                    sumSpans,
                    ref total,
                    ref nextAllowedStart,
                    ref best);
            }

            searchAt++;
        }

        if (best.HasValue)
        {
            AddMatch(best.Value.Match, sumSpans, ref total, ref nextAllowedStart);
        }

        return total;
    }

    private void ProcessBlockCandidateChunk(
        ReadOnlySpan<byte> haystack,
        int baseOffset,
        ulong chunk,
        bool sumSpans,
        ref long total,
        ref int nextAllowedStart,
        ref RegexLiteralSetCandidate? best)
    {
        while (chunk != 0)
        {
            int bit = BitOperations.TrailingZeroCount(chunk);
            int byteOffset = bit / 8;
            int anchorPosition = baseOffset + byteOffset;
            byte blockBits = (byte)(chunk >> (byteOffset * 8));
            chunk &= ~(0xFFUL << (byteOffset * 8));
            while (blockBits != 0)
            {
                int blockIndex = BitOperations.TrailingZeroCount(blockBits);
                blockBits &= (byte)(blockBits - 1);
                ProcessBlockCandidate(
                    haystack,
                    anchorPosition,
                    blockEntriesByMaskBit[blockIndex],
                    sumSpans,
                    ref total,
                    ref nextAllowedStart,
                    ref best);
            }
        }
    }

    private void ProcessBlockCandidate(
        ReadOnlySpan<byte> haystack,
        int anchorPosition,
        RegexAsciiCaseInsensitiveLiteralSetEntry[] entries,
        bool sumSpans,
        ref long total,
        ref int nextAllowedStart,
        ref RegexLiteralSetCandidate? best)
    {
        if (best.HasValue &&
            (anchorPosition >= best.Value.Match.End ||
             anchorPosition - maxAnchorIndex > best.Value.Match.Start))
        {
            AddMatch(best.Value.Match, sumSpans, ref total, ref nextAllowedStart);
            best = null;
        }

        if (anchorPosition < nextAllowedStart)
        {
            return;
        }

        TryAddBestCandidate(haystack, anchorPosition, nextAllowedStart, entries, ref best);
    }

    private static void TryAddBestCandidate(
        ReadOnlySpan<byte> haystack,
        int anchorPosition,
        int minimumStart,
        RegexAsciiCaseInsensitiveLiteralSetEntry[] entries,
        ref RegexLiteralSetCandidate? best)
    {
        if (entries.Length == 1)
        {
            TryAddEntryCandidate(haystack, anchorPosition, minimumStart, entries[0], ref best);
            return;
        }

        for (int index = 0; index < entries.Length; index++)
        {
            TryAddEntryCandidate(haystack, anchorPosition, minimumStart, entries[index], ref best);
        }
    }

    private static void TryAddEntryCandidate(
        ReadOnlySpan<byte> haystack,
        int anchorPosition,
        int minimumStart,
        RegexAsciiCaseInsensitiveLiteralSetEntry entry,
        ref RegexLiteralSetCandidate? best)
    {
        int start = anchorPosition - entry.AnchorIndex;
        byte[] literal = entry.Literal;
        if (start < minimumStart ||
            literal.Length > haystack.Length - start ||
            RegexAsciiCaseInsensitiveFinder.FoldAscii(haystack[start + literal.Length - 1]) != literal[^1] ||
            !MatchesAt(haystack, start, literal))
        {
            return;
        }

        var candidate = new RegexLiteralSetCandidate(
            entry.LiteralId,
            new RegexMatch(start, literal.Length));
        if (IsBetter(candidate, best))
        {
            best = candidate;
        }
    }

    private static bool MatchesAt(ReadOnlySpan<byte> haystack, int start, byte[] literal)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            if (RegexAsciiCaseInsensitiveFinder.FoldAscii(haystack[start + index]) != literal[index])
            {
                return false;
            }
        }

        return true;
    }

    private Vector512<byte> BlockMaskVector512(Vector512<byte> value, int byteIndex)
    {
        var lowNibbleMask = Vector512.Create((byte)0x0F);
        Vector512<byte> lowNibbles = value & lowNibbleMask;
        Vector512<byte> highNibbles = Avx512BW.ShiftRightLogical(value.AsUInt16(), 4).AsByte() & lowNibbleMask;
        return Avx512BW.Shuffle(blockLowMasks512[byteIndex], lowNibbles) &
            Avx512BW.Shuffle(blockHighMasks512[byteIndex], highNibbles);
    }

    private Vector256<byte> BlockMaskVector256(Vector256<byte> value, int byteIndex)
    {
        var lowNibbleMask = Vector256.Create((byte)0x0F);
        Vector256<byte> lowNibbles = Avx2.And(value, lowNibbleMask);
        Vector256<byte> highNibbles = Avx2.And(Avx2.ShiftRightLogical(value.AsUInt16(), 4).AsByte(), lowNibbleMask);
        return Avx2.And(
            Avx2.Shuffle(blockLowMasks256[byteIndex], lowNibbles),
            Avx2.Shuffle(blockHighMasks256[byteIndex], highNibbles));
    }

    private void BuildBlockMasks(List<uint> blocks)
    {
        byte[] low = new byte[64];
        byte[] high = new byte[64];
        for (int byteIndex = 0; byteIndex < BlockLength; byteIndex++)
        {
            Array.Clear(low);
            Array.Clear(high);
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                byte value = (byte)(blocks[blockIndex] >> (byteIndex * 8));
                AddMaskByte(low, high, blockIndex, value);
            }

            ref byte lowReference = ref MemoryMarshal.GetArrayDataReference(low);
            ref byte highReference = ref MemoryMarshal.GetArrayDataReference(high);
            blockLowMasks256[byteIndex] = Vector256.LoadUnsafe(ref lowReference);
            blockHighMasks256[byteIndex] = Vector256.LoadUnsafe(ref highReference);
            blockLowMasks512[byteIndex] = Vector512.LoadUnsafe(ref lowReference);
            blockHighMasks512[byteIndex] = Vector512.LoadUnsafe(ref highReference);
        }
    }

    private static void AddMaskByte(Span<byte> low, Span<byte> high, int blockIndex, byte value)
    {
        byte bit = (byte)(1 << blockIndex);
        int lowNibble = value & 0x0F;
        int highNibble = value >> 4;
        low[lowNibble] |= bit;
        low[lowNibble + 16] |= bit;
        low[lowNibble + 32] |= bit;
        low[lowNibble + 48] |= bit;
        high[highNibble] |= bit;
        high[highNibble + 16] |= bit;
        high[highNibble + 32] |= bit;
        high[highNibble + 48] |= bit;
    }

    private uint FoldedBlockKey(ReadOnlySpan<byte> haystack, int index)
    {
        if (blockBytesAreAsciiLetters)
        {
            return BlockKey(
                (byte)(haystack[index] | 0x20),
                (byte)(haystack[index + 1] | 0x20),
                (byte)(haystack[index + 2] | 0x20));
        }

        return BlockKey(
            RegexAsciiCaseInsensitiveFinder.FoldAscii(haystack[index]),
            RegexAsciiCaseInsensitiveFinder.FoldAscii(haystack[index + 1]),
            RegexAsciiCaseInsensitiveFinder.FoldAscii(haystack[index + 2]));
    }

    private static int SelectAnchorIndex(
        ReadOnlySpan<byte> literal,
        Dictionary<uint, int> blockFrequencies,
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
        Dictionary<uint, int> blockFrequencies,
        int[] byteFrequencies)
    {
        byte first = literal[index];
        byte second = literal[index + 1];
        byte third = literal[index + 2];
        int score = AnchorScore(first) + AnchorScore(second) + AnchorScore(third);
        score -= blockFrequencies[BlockKey(literal[index..])] * 512;
        score -= byteFrequencies[first] * 8;
        score -= byteFrequencies[second] * 8;
        score -= byteFrequencies[third] * 8;
        if (first == (byte)' ' || second == (byte)' ' || third == (byte)' ')
        {
            score -= 256;
        }

        return score;
    }

    private static int AnchorScore(byte value)
    {
        byte folded = RegexAsciiCaseInsensitiveFinder.FoldAscii(value);
        if (folded is >= (byte)'0' and <= (byte)'9')
        {
            return 180;
        }

        if (folded is < (byte)'a' or > (byte)'z')
        {
            return folded == (byte)' ' ? 10 : 220;
        }

        return folded switch
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

    private static Dictionary<uint, int> BuildBlockFrequencies(byte[][] literals)
    {
        var frequencies = new Dictionary<uint, int>();
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            for (int index = 0; index <= literal.Length - BlockLength; index++)
            {
                uint block = BlockKey(literal.AsSpan(index));
                frequencies.TryGetValue(block, out int count);
                frequencies[block] = count + 1;
            }
        }

        return frequencies;
    }

    private static int[] BuildByteFrequencies(byte[][] literals)
    {
        int[] frequencies = new int[256];
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            for (int index = 0; index < literal.Length; index++)
            {
                frequencies[literal[index]]++;
            }
        }

        return frequencies;
    }

    private static byte[][] NormalizeAsciiCase(IReadOnlyList<byte[]> literals)
    {
        byte[][] normalized = new byte[literals.Count][];
        for (int index = 0; index < literals.Count; index++)
        {
            normalized[index] = NormalizeAsciiCase(literals[index]);
        }

        return normalized;
    }

    private static byte[] NormalizeAsciiCase(byte[] literal)
    {
        byte[] normalized = literal.ToArray();
        for (int index = 0; index < normalized.Length; index++)
        {
            normalized[index] = RegexAsciiCaseInsensitiveFinder.FoldAscii(normalized[index]);
        }

        return normalized;
    }

    private static Vector256<byte> FoldAsciiVector256(Vector256<byte> value)
    {
        Vector256<sbyte> signed = value.AsSByte();
        Vector256<byte> aboveBeforeA = Avx2.CompareGreaterThan(
            signed,
            Vector256.Create((sbyte)((byte)'A' - 1))).AsByte();
        Vector256<byte> belowAfterZ = Avx2.CompareGreaterThan(
            Vector256.Create((sbyte)((byte)'Z' + 1)),
            signed).AsByte();
        Vector256<byte> uppercase = Avx2.And(aboveBeforeA, belowAfterZ);
        return Avx2.Or(value, Avx2.And(uppercase, Vector256.Create((byte)0x20)));
    }

    private static Vector512<byte> FoldAsciiVector512(Vector512<byte> value)
    {
        Vector512<sbyte> signed = value.AsSByte();
        Vector512<byte> aboveBeforeA = Avx512BW.CompareGreaterThan(
            signed,
            Vector512.Create((sbyte)((byte)'A' - 1))).AsByte();
        Vector512<byte> belowAfterZ = Avx512BW.CompareGreaterThan(
            Vector512.Create((sbyte)((byte)'Z' + 1)),
            signed).AsByte();
        Vector512<byte> uppercase = aboveBeforeA & belowAfterZ;
        return value | (uppercase & Vector512.Create((byte)0x20));
    }

    private static uint BlockKey(ReadOnlySpan<byte> value)
    {
        return BlockKey(value[0], value[1], value[2]);
    }

    private static uint BlockKey(byte first, byte second, byte third)
    {
        return (uint)(first | (second << 8) | (third << 16));
    }

    private static bool BlocksAreAsciiLetters(List<uint> blocks)
    {
        for (int index = 0; index < blocks.Count; index++)
        {
            uint block = blocks[index];
            if (!IsAsciiLowercaseLetter((byte)block) ||
                !IsAsciiLowercaseLetter((byte)(block >> 8)) ||
                !IsAsciiLowercaseLetter((byte)(block >> 16)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLowercaseLetter(byte value)
    {
        return value is >= (byte)'a' and <= (byte)'z';
    }

    private static bool CanUse(IReadOnlyList<byte[]> literals)
    {
        if (literals.Count is < MinimumLiteralCount or > MaximumLiteralCount)
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

        return true;
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
