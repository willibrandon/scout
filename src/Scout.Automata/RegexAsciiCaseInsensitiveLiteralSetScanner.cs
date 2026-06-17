using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexAsciiCaseInsensitiveLiteralSetScanner
{
    private const int BlockLength = 2;

    private readonly RegexAsciiCaseInsensitiveLiteralSetEntry[][] entriesByAnchorByte = [];
    private readonly SearchValues<byte>? anchorBytes;
    private readonly Dictionary<ushort, RegexAsciiCaseInsensitiveLiteralSetEntry[]>? entriesByBlock;
    private readonly byte[] blockFirstBytes = [];
    private readonly byte[] blockSecondBytes = [];
    private readonly bool[] blockFirstHasAlternate = [];
    private readonly bool[] blockSecondHasAlternate = [];
    private readonly Vector128<byte>[] blockFirstVectors128 = [];
    private readonly Vector128<byte>[] blockSecondVectors128 = [];
    private readonly Vector128<byte>[] blockFirstAlternateVectors128 = [];
    private readonly Vector128<byte>[] blockSecondAlternateVectors128 = [];
    private readonly Vector256<byte>[] blockFirstVectors256 = [];
    private readonly Vector256<byte>[] blockSecondVectors256 = [];
    private readonly Vector256<byte>[] blockFirstAlternateVectors256 = [];
    private readonly Vector256<byte>[] blockSecondAlternateVectors256 = [];
    private readonly int maxAnchorIndex;

    public RegexAsciiCaseInsensitiveLiteralSetScanner(IReadOnlyList<byte[]> literals)
    {
        if (CanUseBlockScanner(literals))
        {
            byte[][] normalized = NormalizeAsciiCase(literals);
            Dictionary<ushort, int> blockFrequencies = BuildBlockFrequencies(normalized);
            int[] byteFrequencies = BuildByteFrequencies(normalized);
            var buckets = new Dictionary<ushort, List<RegexAsciiCaseInsensitiveLiteralSetEntry>>();
            var distinctBlocks = new List<ushort>();
            for (int index = 0; index < normalized.Length; index++)
            {
                byte[] literal = normalized[index];
                int anchorIndex = SelectAnchorIndex(literal, blockFrequencies, byteFrequencies);
                maxAnchorIndex = Math.Max(maxAnchorIndex, anchorIndex);

                ushort block = BlockKey(literal.AsSpan(anchorIndex));
                if (!buckets.TryGetValue(block, out List<RegexAsciiCaseInsensitiveLiteralSetEntry>? entries))
                {
                    entries = [];
                    buckets.Add(block, entries);
                    distinctBlocks.Add(block);
                }

                entries.Add(new RegexAsciiCaseInsensitiveLiteralSetEntry(index, literal, anchorIndex));
            }

            entriesByBlock = new Dictionary<ushort, RegexAsciiCaseInsensitiveLiteralSetEntry[]>(buckets.Count);
            foreach (KeyValuePair<ushort, List<RegexAsciiCaseInsensitiveLiteralSetEntry>> bucket in buckets)
            {
                entriesByBlock.Add(bucket.Key, bucket.Value.ToArray());
            }

            blockFirstBytes = new byte[distinctBlocks.Count];
            blockSecondBytes = new byte[distinctBlocks.Count];
            blockFirstHasAlternate = new bool[distinctBlocks.Count];
            blockSecondHasAlternate = new bool[distinctBlocks.Count];
            blockFirstVectors128 = new Vector128<byte>[distinctBlocks.Count];
            blockSecondVectors128 = new Vector128<byte>[distinctBlocks.Count];
            blockFirstAlternateVectors128 = new Vector128<byte>[distinctBlocks.Count];
            blockSecondAlternateVectors128 = new Vector128<byte>[distinctBlocks.Count];
            blockFirstVectors256 = new Vector256<byte>[distinctBlocks.Count];
            blockSecondVectors256 = new Vector256<byte>[distinctBlocks.Count];
            blockFirstAlternateVectors256 = new Vector256<byte>[distinctBlocks.Count];
            blockSecondAlternateVectors256 = new Vector256<byte>[distinctBlocks.Count];
            for (int index = 0; index < distinctBlocks.Count; index++)
            {
                ushort block = distinctBlocks[index];
                byte first = (byte)block;
                byte second = (byte)(block >> 8);
                byte firstAlternate = RegexAsciiCaseInsensitiveFinder.ToggleAsciiCase(first);
                byte secondAlternate = RegexAsciiCaseInsensitiveFinder.ToggleAsciiCase(second);
                blockFirstBytes[index] = first;
                blockSecondBytes[index] = second;
                blockFirstHasAlternate[index] = RegexAsciiCaseInsensitiveFinder.IsAsciiCased(first);
                blockSecondHasAlternate[index] = RegexAsciiCaseInsensitiveFinder.IsAsciiCased(second);
                blockFirstVectors128[index] = Vector128.Create(first);
                blockSecondVectors128[index] = Vector128.Create(second);
                blockFirstAlternateVectors128[index] = Vector128.Create(firstAlternate);
                blockSecondAlternateVectors128[index] = Vector128.Create(secondAlternate);
                blockFirstVectors256[index] = Vector256.Create(first);
                blockSecondVectors256[index] = Vector256.Create(second);
                blockFirstAlternateVectors256[index] = Vector256.Create(firstAlternate);
                blockSecondAlternateVectors256[index] = Vector256.Create(secondAlternate);
            }

            return;
        }

        var anchorBuckets = new List<RegexAsciiCaseInsensitiveLiteralSetEntry>[256];
        var distinctAnchorBytes = new List<byte>();
        for (int index = 0; index < literals.Count; index++)
        {
            byte[] normalized = NormalizeAsciiCase(literals[index]);
            int anchorIndex = RegexAsciiCaseInsensitiveFinder.SelectAnchorIndex(normalized);
            maxAnchorIndex = Math.Max(maxAnchorIndex, anchorIndex);

            byte anchor = normalized[anchorIndex];
            var entry = new RegexAsciiCaseInsensitiveLiteralSetEntry(index, normalized, anchorIndex);
            AddEntry(anchorBuckets, distinctAnchorBytes, anchor, entry);
            if (RegexAsciiCaseInsensitiveFinder.IsAsciiCased(anchor))
            {
                AddEntry(
                    anchorBuckets,
                    distinctAnchorBytes,
                    RegexAsciiCaseInsensitiveFinder.ToggleAsciiCase(anchor),
                    entry);
            }
        }

        entriesByAnchorByte = new RegexAsciiCaseInsensitiveLiteralSetEntry[256][];
        for (int index = 0; index < anchorBuckets.Length; index++)
        {
            entriesByAnchorByte[index] = anchorBuckets[index]?.ToArray() ?? [];
        }

        anchorBytes = SearchValues.Create(distinctAnchorBytes.ToArray());
    }

    public RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        return entriesByBlock is not null
            ? FindBlock(haystack, startAt)
            : FindAnchor(haystack, startAt);
    }

    public long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        return entriesByBlock is not null
            ? CountOrSumBlock(haystack, startAt, sumSpans)
            : CountOrSumAnchor(haystack, startAt, sumSpans);
    }

    private RegexLiteralSetCandidate? FindBlock(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = startOffset;
        RegexLiteralSetCandidate? best = null;
        while (searchAt <= haystack.Length - BlockLength)
        {
            int anchorPosition = FindBlockPosition(haystack, searchAt);
            if (anchorPosition < 0)
            {
                break;
            }

            if (best.HasValue && anchorPosition - maxAnchorIndex > best.Value.Match.Start)
            {
                break;
            }

            ushort block = FoldedBlockKey(haystack, anchorPosition);
            TryAddBestCandidate(haystack, anchorPosition, startOffset, entriesByBlock![block], ref best);

            searchAt = anchorPosition + 1;
        }

        return best;
    }

    private long CountOrSumBlock(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        int nextAllowedStart = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = nextAllowedStart;
        long total = 0;
        RegexLiteralSetCandidate? best = null;
        while (searchAt <= haystack.Length - BlockLength)
        {
            int anchorPosition = FindBlockPosition(haystack, searchAt);
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

            ushort block = FoldedBlockKey(haystack, anchorPosition);
            TryAddBestCandidate(haystack, anchorPosition, nextAllowedStart, entriesByBlock![block], ref best);

            searchAt = anchorPosition + 1;
        }

        if (best.HasValue)
        {
            AddMatch(best.Value.Match, sumSpans, ref total, ref nextAllowedStart);
        }

        return total;
    }

    private RegexLiteralSetCandidate? FindAnchor(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = startOffset;
        RegexLiteralSetCandidate? best = null;
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(anchorBytes!);
            if (offset < 0)
            {
                break;
            }

            int anchorPosition = searchAt + offset;
            if (best.HasValue && anchorPosition - maxAnchorIndex > best.Value.Match.Start)
            {
                break;
            }

            TryAddBestCandidate(
                haystack,
                anchorPosition,
                startOffset,
                entriesByAnchorByte[haystack[anchorPosition]],
                ref best);

            searchAt = anchorPosition + 1;
        }

        return best;
    }

    private long CountOrSumAnchor(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        int nextAllowedStart = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = nextAllowedStart;
        long total = 0;
        RegexLiteralSetCandidate? best = null;
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(anchorBytes!);
            if (offset < 0)
            {
                break;
            }

            int anchorPosition = searchAt + offset;
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

            TryAddBestCandidate(
                haystack,
                anchorPosition,
                nextAllowedStart,
                entriesByAnchorByte[haystack[anchorPosition]],
                ref best);

            searchAt = anchorPosition + 1;
        }

        if (best.HasValue)
        {
            AddMatch(best.Value.Match, sumSpans, ref total, ref nextAllowedStart);
        }

        return total;
    }

    private int FindBlockPosition(ReadOnlySpan<byte> haystack, int startAt)
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
                Vector256<byte> firstMatches = Avx2.CompareEqual(current, blockFirstVectors256[index]);
                if (blockFirstHasAlternate[index])
                {
                    firstMatches = Avx2.Or(
                        firstMatches,
                        Avx2.CompareEqual(current, blockFirstAlternateVectors256[index]));
                }

                Vector256<byte> secondMatches = Avx2.CompareEqual(next, blockSecondVectors256[index]);
                if (blockSecondHasAlternate[index])
                {
                    secondMatches = Avx2.Or(
                        secondMatches,
                        Avx2.CompareEqual(next, blockSecondAlternateVectors256[index]));
                }

                matches = Avx2.Or(matches, Avx2.And(firstMatches, secondMatches));
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
                Vector128<byte> firstMatches = Sse2.CompareEqual(current, blockFirstVectors128[index]);
                if (blockFirstHasAlternate[index])
                {
                    firstMatches = Sse2.Or(
                        firstMatches,
                        Sse2.CompareEqual(current, blockFirstAlternateVectors128[index]));
                }

                Vector128<byte> secondMatches = Sse2.CompareEqual(next, blockSecondVectors128[index]);
                if (blockSecondHasAlternate[index])
                {
                    secondMatches = Sse2.Or(
                        secondMatches,
                        Sse2.CompareEqual(next, blockSecondAlternateVectors128[index]));
                }

                matches = Sse2.Or(matches, Sse2.And(firstMatches, secondMatches));
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
            if (entriesByBlock!.ContainsKey(FoldedBlockKey(haystack, index)))
            {
                return index;
            }
        }

        return -1;
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

    private static bool CanUseBlockScanner(IReadOnlyList<byte[]> literals)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].Length < BlockLength)
            {
                return false;
            }
        }

        return true;
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
        if (first == (byte)' ' || second == (byte)' ')
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

    private static Dictionary<ushort, int> BuildBlockFrequencies(byte[][] literals)
    {
        var frequencies = new Dictionary<ushort, int>();
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
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

    private static ushort FoldedBlockKey(ReadOnlySpan<byte> haystack, int index)
    {
        return BlockKey(
            RegexAsciiCaseInsensitiveFinder.FoldAscii(haystack[index]),
            RegexAsciiCaseInsensitiveFinder.FoldAscii(haystack[index + 1]));
    }

    private static ushort BlockKey(ReadOnlySpan<byte> value)
    {
        return BlockKey(value[0], value[1]);
    }

    private static ushort BlockKey(byte first, byte second)
    {
        return (ushort)(first | (second << 8));
    }

    private static void AddEntry(
        List<RegexAsciiCaseInsensitiveLiteralSetEntry>[] buckets,
        List<byte> distinctAnchorBytes,
        byte anchor,
        RegexAsciiCaseInsensitiveLiteralSetEntry entry)
    {
        (buckets[anchor] ??= []).Add(entry);
        AddDistinct(distinctAnchorBytes, anchor);
    }

    private static void AddDistinct(List<byte> bytes, byte value)
    {
        for (int index = 0; index < bytes.Count; index++)
        {
            if (bytes[index] == value)
            {
                return;
            }
        }

        bytes.Add(value);
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
