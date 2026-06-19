using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexPackedLiteralSetScanner
{
    private const int BucketCount = 8;
    private const int MaskLength = 4;
    private const int MinimumLiteralCount = 3;
    private const int MaximumLiteralCount = 8;

    private readonly byte[][] literals;
    private readonly int[][]? commonFoldedLiterals;
    private readonly byte[][][]? prefixByteVariants;
    private readonly int anchorOffset;
    private readonly int[][] buckets;
    private readonly int[]? singleLiteralBuckets;
    private readonly Vector128<byte>[] lowMasks128 = new Vector128<byte>[MaskLength];
    private readonly Vector128<byte>[] highMasks128 = new Vector128<byte>[MaskLength];
    private readonly Vector256<byte>[] lowMasks256 = new Vector256<byte>[MaskLength];
    private readonly Vector256<byte>[] highMasks256 = new Vector256<byte>[MaskLength];
    private readonly bool useDenseCandidateCount;

    private RegexPackedLiteralSetScanner(
        IReadOnlyList<byte[]> literals,
        int[][]? commonFoldedLiterals = null,
        byte[][][]? prefixByteVariants = null)
    {
        this.literals = new byte[literals.Count][];
        for (int index = 0; index < literals.Count; index++)
        {
            this.literals[index] = literals[index].ToArray();
        }

        this.commonFoldedLiterals = commonFoldedLiterals;
        this.prefixByteVariants = prefixByteVariants;
        anchorOffset = prefixByteVariants is null ? SelectAnchorOffset(this.literals) : 0;
        useDenseCandidateCount = !ContainsNonAscii(this.literals) || ContainsAsciiOrTwoByteUtf8Only(this.literals);
        buckets = BuildBuckets(this.literals, anchorOffset);
        singleLiteralBuckets = TryBuildSingleLiteralBuckets(buckets);
        BuildMasks();
    }

    public static bool TryCreate(IReadOnlyList<byte[]> literals, out RegexPackedLiteralSetScanner? scanner)
    {
        return TryCreateWithMaxLiteralCount(literals, MaximumLiteralCount, out scanner);
    }

    public static bool TryCreateWithMaxLiteralCount(
        IReadOnlyList<byte[]> literals,
        int maxLiteralCount,
        out RegexPackedLiteralSetScanner? scanner)
    {
        scanner = null;
        if (literals.Count < MinimumLiteralCount || literals.Count > maxLiteralCount)
        {
            return false;
        }

        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].Length < MaskLength)
            {
                return false;
            }
        }

        scanner = new RegexPackedLiteralSetScanner(literals);
        return true;
    }

    public static bool TryCreateCommonCyrillicCaseInsensitive(
        IReadOnlyList<byte[]> literals,
        out RegexPackedLiteralSetScanner? scanner)
    {
        scanner = null;
        if (literals.Count is < MinimumLiteralCount or > MaximumLiteralCount)
        {
            return false;
        }

        int[][] folded = new int[literals.Count][];
        byte[][][] prefixVariants = new byte[literals.Count][][];
        for (int index = 0; index < literals.Count; index++)
        {
            byte[] literal = literals[index];
            if (literal.Length < MaskLength ||
                !TryBuildCommonFoldedLiteral(literal, out folded[index]) ||
                !TryBuildCommonCyrillicPrefixByteVariants(literal, out prefixVariants[index]))
            {
                return false;
            }
        }

        scanner = new RegexPackedLiteralSetScanner(literals, folded, prefixVariants);
        return true;
    }

    public RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt <= MaxCandidateStart(haystack.Length))
        {
            int candidateStart = FindCandidate(haystack, searchAt, out byte bucketBits);
            if (candidateStart < 0)
            {
                return null;
            }

            if (TryVerifyAt(haystack, candidateStart, bucketBits, out RegexLiteralSetCandidate candidate))
            {
                return candidate;
            }

            searchAt = candidateStart + 1;
        }

        return null;
    }

    public long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (!useDenseCandidateCount)
        {
            return CountOrSumByFirstCandidate(haystack, startOffset, sumSpans);
        }

        if (Avx2.IsSupported && CanUseVector256(haystack.Length, startOffset))
        {
            return CountOrSumVector256(haystack, startOffset, sumSpans);
        }

        if (Ssse3.IsSupported && CanUseVector128(haystack.Length, startOffset))
        {
            return CountOrSumVector128(haystack, startOffset, sumSpans);
        }

        return CountOrSumScalar(haystack, startOffset, sumSpans, total: 0);
    }

    private long CountOrSumByFirstCandidate(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        int searchAt = startAt;
        long total = 0;
        while (searchAt <= MaxCandidateStart(haystack.Length))
        {
            int candidateStart = FindCandidate(haystack, searchAt, out byte bucketBits);
            if (candidateStart < 0)
            {
                return total;
            }

            if (TryVerifyAt(haystack, candidateStart, bucketBits, out RegexLiteralSetCandidate candidate))
            {
                RegexMatch match = candidate.Match;
                total += sumSpans ? match.Length : 1;
                searchAt = match.End;
            }
            else
            {
                searchAt = candidateStart + 1;
            }
        }

        return total;
    }

    private long CountOrSumVector256(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int nextAllowedStart = startAt;
        int offset = startAt + anchorOffset + MaskLength - 1;
        int vectorEnd = haystack.Length - Vector256<byte>.Count;
        long total = 0;
        var previous0 = Vector256.Create(byte.MaxValue);
        var previous1 = Vector256.Create(byte.MaxValue);
        var previous2 = Vector256.Create(byte.MaxValue);
        int unrolledEnd = vectorEnd - Vector256<byte>.Count * 3;
        while (offset <= unrolledEnd)
        {
            var firstChunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> firstCandidates = CandidateVector256(firstChunk, ref previous0, ref previous1, ref previous2);
            if (Avx2.CompareEqual(firstCandidates, Vector256<byte>.Zero).ExtractMostSignificantBits() != uint.MaxValue)
            {
                int baseOffset = offset - MaskLength + 1 - anchorOffset;
                for (int lane = 0; lane < 4; lane++)
                {
                    CountOrSumCandidateChunk(
                        haystack,
                        baseOffset + lane * 8,
                        firstCandidates.AsUInt64().GetElement(lane),
                        sumSpans,
                        ref total,
                        ref nextAllowedStart);
                }
            }

            offset += Vector256<byte>.Count;
            var secondChunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> secondCandidates = CandidateVector256(secondChunk, ref previous0, ref previous1, ref previous2);
            if (Avx2.CompareEqual(secondCandidates, Vector256<byte>.Zero).ExtractMostSignificantBits() != uint.MaxValue)
            {
                int baseOffset = offset - MaskLength + 1 - anchorOffset;
                for (int lane = 0; lane < 4; lane++)
                {
                    CountOrSumCandidateChunk(
                        haystack,
                        baseOffset + lane * 8,
                        secondCandidates.AsUInt64().GetElement(lane),
                        sumSpans,
                        ref total,
                        ref nextAllowedStart);
                }
            }

            offset += Vector256<byte>.Count;
            var thirdChunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> thirdCandidates = CandidateVector256(thirdChunk, ref previous0, ref previous1, ref previous2);
            if (Avx2.CompareEqual(thirdCandidates, Vector256<byte>.Zero).ExtractMostSignificantBits() != uint.MaxValue)
            {
                int baseOffset = offset - MaskLength + 1 - anchorOffset;
                for (int lane = 0; lane < 4; lane++)
                {
                    CountOrSumCandidateChunk(
                        haystack,
                        baseOffset + lane * 8,
                        thirdCandidates.AsUInt64().GetElement(lane),
                        sumSpans,
                        ref total,
                        ref nextAllowedStart);
                }
            }

            offset += Vector256<byte>.Count;
            var fourthChunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> fourthCandidates = CandidateVector256(fourthChunk, ref previous0, ref previous1, ref previous2);
            if (Avx2.CompareEqual(fourthCandidates, Vector256<byte>.Zero).ExtractMostSignificantBits() != uint.MaxValue)
            {
                int baseOffset = offset - MaskLength + 1 - anchorOffset;
                for (int lane = 0; lane < 4; lane++)
                {
                    CountOrSumCandidateChunk(
                        haystack,
                        baseOffset + lane * 8,
                        fourthCandidates.AsUInt64().GetElement(lane),
                        sumSpans,
                        ref total,
                        ref nextAllowedStart);
                }
            }

            offset += Vector256<byte>.Count;
        }

        while (offset <= vectorEnd)
        {
            var chunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> candidates = CandidateVector256(chunk, ref previous0, ref previous1, ref previous2);
            if (Avx2.CompareEqual(candidates, Vector256<byte>.Zero).ExtractMostSignificantBits() != uint.MaxValue)
            {
                int baseOffset = offset - MaskLength + 1 - anchorOffset;
                for (int lane = 0; lane < 4; lane++)
                {
                    CountOrSumCandidateChunk(
                        haystack,
                        baseOffset + lane * 8,
                        candidates.AsUInt64().GetElement(lane),
                        sumSpans,
                        ref total,
                        ref nextAllowedStart);
                }
            }

            offset += Vector256<byte>.Count;
        }

        return CountOrSumScalar(haystack, Math.Max(nextAllowedStart, offset - MaskLength + 1 - anchorOffset), sumSpans, total);
    }

    private long CountOrSumVector128(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int nextAllowedStart = startAt;
        int offset = startAt + anchorOffset + MaskLength - 1;
        int vectorEnd = haystack.Length - Vector128<byte>.Count;
        long total = 0;
        var previous0 = Vector128.Create(byte.MaxValue);
        var previous1 = Vector128.Create(byte.MaxValue);
        var previous2 = Vector128.Create(byte.MaxValue);
        while (offset <= vectorEnd)
        {
            var chunk = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            Vector128<byte> candidates = CandidateVector128(chunk, ref previous0, ref previous1, ref previous2);
            if (Sse2.CompareEqual(candidates, Vector128<byte>.Zero).ExtractMostSignificantBits() != 0xFFFF)
            {
                int baseOffset = offset - MaskLength + 1 - anchorOffset;
                for (int lane = 0; lane < 2; lane++)
                {
                    CountOrSumCandidateChunk(
                        haystack,
                        baseOffset + lane * 8,
                        candidates.AsUInt64().GetElement(lane),
                        sumSpans,
                        ref total,
                        ref nextAllowedStart);
                }
            }

            offset += Vector128<byte>.Count;
        }

        return CountOrSumScalar(haystack, Math.Max(nextAllowedStart, offset - MaskLength + 1 - anchorOffset), sumSpans, total);
    }

    private long CountOrSumScalar(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, long total)
    {
        int searchAt = startAt;
        while (searchAt <= MaxCandidateStart(haystack.Length))
        {
            int candidateStart = FindCandidateScalar(haystack, searchAt, out byte bucketBits);
            if (candidateStart < 0)
            {
                return total;
            }

            if (TryVerifyAt(haystack, candidateStart, bucketBits, out RegexLiteralSetCandidate candidate))
            {
                RegexMatch match = candidate.Match;
                total += sumSpans ? match.Length : 1;
                searchAt = match.End;
            }
            else
            {
                searchAt = candidateStart + 1;
            }
        }

        return total;
    }

    private void CountOrSumCandidateChunk(
        ReadOnlySpan<byte> haystack,
        int baseOffset,
        ulong chunk,
        bool sumSpans,
        ref long total,
        ref int nextAllowedStart)
    {
        while (chunk != 0)
        {
            int bit = BitOperations.TrailingZeroCount(chunk);
            int byteOffset = bit / BucketCount;
            int candidateStart = baseOffset + byteOffset;
            byte bucketBits = (byte)(chunk >> (byteOffset * BucketCount));
            chunk &= ~(0xFFUL << (byteOffset * BucketCount));
            if (candidateStart < nextAllowedStart)
            {
                continue;
            }

            if (!TryVerifyAt(haystack, candidateStart, bucketBits, out RegexLiteralSetCandidate candidate))
            {
                continue;
            }

            RegexMatch match = candidate.Match;
            total += sumSpans ? match.Length : 1;
            nextAllowedStart = match.End;
        }
    }

    private int FindCandidate(ReadOnlySpan<byte> haystack, int startAt, out byte bucketBits)
    {
        if (startAt > MaxCandidateStart(haystack.Length))
        {
            bucketBits = 0;
            return -1;
        }

        if (Avx2.IsSupported && CanUseVector256(haystack.Length, startAt))
        {
            return FindCandidateVector256(haystack, startAt, out bucketBits);
        }

        if (Ssse3.IsSupported && CanUseVector128(haystack.Length, startAt))
        {
            return FindCandidateVector128(haystack, startAt, out bucketBits);
        }

        return FindCandidateScalar(haystack, startAt, out bucketBits);
    }

    private int FindCandidateVector256(ReadOnlySpan<byte> haystack, int startAt, out byte bucketBits)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt + anchorOffset + MaskLength - 1;
        int vectorEnd = haystack.Length - Vector256<byte>.Count;
        var previous0 = Vector256.Create(byte.MaxValue);
        var previous1 = Vector256.Create(byte.MaxValue);
        var previous2 = Vector256.Create(byte.MaxValue);
        int unrolledEnd = vectorEnd - Vector256<byte>.Count * 3;
        while (offset <= unrolledEnd)
        {
            var firstChunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> firstCandidates = CandidateVector256(firstChunk, ref previous0, ref previous1, ref previous2);
            if (TryGetFirstCandidate(firstCandidates, offset - MaskLength + 1 - anchorOffset, out int candidateStart, out bucketBits))
            {
                return candidateStart;
            }

            offset += Vector256<byte>.Count;
            var secondChunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> secondCandidates = CandidateVector256(secondChunk, ref previous0, ref previous1, ref previous2);
            if (TryGetFirstCandidate(secondCandidates, offset - MaskLength + 1 - anchorOffset, out candidateStart, out bucketBits))
            {
                return candidateStart;
            }

            offset += Vector256<byte>.Count;
            var thirdChunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> thirdCandidates = CandidateVector256(thirdChunk, ref previous0, ref previous1, ref previous2);
            if (TryGetFirstCandidate(thirdCandidates, offset - MaskLength + 1 - anchorOffset, out candidateStart, out bucketBits))
            {
                return candidateStart;
            }

            offset += Vector256<byte>.Count;
            var fourthChunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> fourthCandidates = CandidateVector256(fourthChunk, ref previous0, ref previous1, ref previous2);
            if (TryGetFirstCandidate(fourthCandidates, offset - MaskLength + 1 - anchorOffset, out candidateStart, out bucketBits))
            {
                return candidateStart;
            }

            offset += Vector256<byte>.Count;
        }

        while (offset <= vectorEnd)
        {
            var chunk = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            Vector256<byte> candidates = CandidateVector256(chunk, ref previous0, ref previous1, ref previous2);
            if (TryGetFirstCandidate(candidates, offset - MaskLength + 1 - anchorOffset, out int candidateStart, out bucketBits))
            {
                return candidateStart;
            }

            offset += Vector256<byte>.Count;
        }

        return FindCandidateScalar(haystack, Math.Max(startAt, offset - MaskLength + 1 - anchorOffset), out bucketBits);
    }

    private int FindCandidateVector128(ReadOnlySpan<byte> haystack, int startAt, out byte bucketBits)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt + anchorOffset + MaskLength - 1;
        int vectorEnd = haystack.Length - Vector128<byte>.Count;
        var previous0 = Vector128.Create(byte.MaxValue);
        var previous1 = Vector128.Create(byte.MaxValue);
        var previous2 = Vector128.Create(byte.MaxValue);
        while (offset <= vectorEnd)
        {
            var chunk = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            Vector128<byte> candidates = CandidateVector128(chunk, ref previous0, ref previous1, ref previous2);
            if (TryGetFirstCandidate(candidates, offset - MaskLength + 1 - anchorOffset, out int candidateStart, out bucketBits))
            {
                return candidateStart;
            }

            offset += Vector128<byte>.Count;
        }

        return FindCandidateScalar(haystack, Math.Max(startAt, offset - MaskLength + 1 - anchorOffset), out bucketBits);
    }

    private int FindCandidateScalar(ReadOnlySpan<byte> haystack, int startAt, out byte bucketBits)
    {
        for (int position = startAt; position <= MaxCandidateStart(haystack.Length); position++)
        {
            byte bits = ExactPrefixBucketBits(haystack[(position + anchorOffset)..]);
            if (bits != 0)
            {
                bucketBits = bits;
                return position;
            }
        }

        bucketBits = 0;
        return -1;
    }

    private int MaxCandidateStart(int haystackLength)
    {
        return haystackLength - MaskLength - anchorOffset;
    }

    private bool CanUseVector256(int haystackLength, int startAt)
    {
        int anchorStart = startAt + anchorOffset;
        return haystackLength - anchorStart >= Vector256<byte>.Count + MaskLength - 1;
    }

    private bool CanUseVector128(int haystackLength, int startAt)
    {
        int anchorStart = startAt + anchorOffset;
        return haystackLength - anchorStart >= Vector128<byte>.Count + MaskLength - 1;
    }

    private Vector256<byte> CandidateVector256(
        Vector256<byte> chunk,
        ref Vector256<byte> previous0,
        ref Vector256<byte> previous1,
        ref Vector256<byte> previous2)
    {
        var lowNibbleMask = Vector256.Create((byte)0x0F);
        Vector256<byte> lowNibbles = Avx2.And(chunk, lowNibbleMask);
        Vector256<byte> highNibbles = Avx2.And(Avx2.ShiftRightLogical(chunk.AsUInt16(), 4).AsByte(), lowNibbleMask);

        Vector256<byte> candidate0 = Avx2.And(
            Avx2.Shuffle(lowMasks256[0], lowNibbles),
            Avx2.Shuffle(highMasks256[0], highNibbles));
        Vector256<byte> candidate1 = Avx2.And(
            Avx2.Shuffle(lowMasks256[1], lowNibbles),
            Avx2.Shuffle(highMasks256[1], highNibbles));
        Vector256<byte> candidate2 = Avx2.And(
            Avx2.Shuffle(lowMasks256[2], lowNibbles),
            Avx2.Shuffle(highMasks256[2], highNibbles));
        Vector256<byte> candidate3 = Avx2.And(
            Avx2.Shuffle(lowMasks256[3], lowNibbles),
            Avx2.Shuffle(highMasks256[3], highNibbles));

        Vector256<byte> aligned0 = ShiftInThreeBytes(candidate0, previous0);
        Vector256<byte> aligned1 = ShiftInTwoBytes(candidate1, previous1);
        Vector256<byte> aligned2 = ShiftInOneByte(candidate2, previous2);
        previous0 = candidate0;
        previous1 = candidate1;
        previous2 = candidate2;

        return Avx2.And(Avx2.And(aligned0, aligned1), Avx2.And(aligned2, candidate3));
    }

    private Vector128<byte> CandidateVector128(
        Vector128<byte> chunk,
        ref Vector128<byte> previous0,
        ref Vector128<byte> previous1,
        ref Vector128<byte> previous2)
    {
        var lowNibbleMask = Vector128.Create((byte)0x0F);
        Vector128<byte> lowNibbles = Sse2.And(chunk, lowNibbleMask);
        Vector128<byte> highNibbles = Sse2.And(Sse2.ShiftRightLogical(chunk.AsUInt16(), 4).AsByte(), lowNibbleMask);

        Vector128<byte> candidate0 = Sse2.And(
            Ssse3.Shuffle(lowMasks128[0], lowNibbles),
            Ssse3.Shuffle(highMasks128[0], highNibbles));
        Vector128<byte> candidate1 = Sse2.And(
            Ssse3.Shuffle(lowMasks128[1], lowNibbles),
            Ssse3.Shuffle(highMasks128[1], highNibbles));
        Vector128<byte> candidate2 = Sse2.And(
            Ssse3.Shuffle(lowMasks128[2], lowNibbles),
            Ssse3.Shuffle(highMasks128[2], highNibbles));
        Vector128<byte> candidate3 = Sse2.And(
            Ssse3.Shuffle(lowMasks128[3], lowNibbles),
            Ssse3.Shuffle(highMasks128[3], highNibbles));

        Vector128<byte> aligned0 = Ssse3.AlignRight(candidate0, previous0, 13);
        Vector128<byte> aligned1 = Ssse3.AlignRight(candidate1, previous1, 14);
        Vector128<byte> aligned2 = Ssse3.AlignRight(candidate2, previous2, 15);
        previous0 = candidate0;
        previous1 = candidate1;
        previous2 = candidate2;

        return Sse2.And(Sse2.And(aligned0, aligned1), Sse2.And(aligned2, candidate3));
    }

    private static Vector256<byte> ShiftInOneByte(Vector256<byte> current, Vector256<byte> previous)
    {
        Vector256<byte> adjacent = Avx2.Permute2x128(previous, current, 0x21);
        return Avx2.AlignRight(current, adjacent, 15);
    }

    private static Vector256<byte> ShiftInTwoBytes(Vector256<byte> current, Vector256<byte> previous)
    {
        Vector256<byte> adjacent = Avx2.Permute2x128(previous, current, 0x21);
        return Avx2.AlignRight(current, adjacent, 14);
    }

    private static Vector256<byte> ShiftInThreeBytes(Vector256<byte> current, Vector256<byte> previous)
    {
        Vector256<byte> adjacent = Avx2.Permute2x128(previous, current, 0x21);
        return Avx2.AlignRight(current, adjacent, 13);
    }

    private static bool TryGetFirstCandidate(
        Vector256<byte> candidates,
        int baseOffset,
        out int candidateStart,
        out byte bucketBits)
    {
        if (Avx2.CompareEqual(candidates, Vector256<byte>.Zero).ExtractMostSignificantBits() == uint.MaxValue)
        {
            candidateStart = -1;
            bucketBits = 0;
            return false;
        }

        for (int lane = 0; lane < 4; lane++)
        {
            ulong chunk = candidates.AsUInt64().GetElement(lane);
            if (chunk == 0)
            {
                continue;
            }

            int bit = BitOperations.TrailingZeroCount(chunk);
            int byteOffset = bit / BucketCount;
            candidateStart = baseOffset + lane * 8 + byteOffset;
            bucketBits = (byte)(chunk >> (byteOffset * BucketCount));
            return true;
        }

        candidateStart = -1;
        bucketBits = 0;
        return false;
    }

    private static bool TryGetFirstCandidate(
        Vector128<byte> candidates,
        int baseOffset,
        out int candidateStart,
        out byte bucketBits)
    {
        if (Sse2.CompareEqual(candidates, Vector128<byte>.Zero).ExtractMostSignificantBits() == 0xFFFF)
        {
            candidateStart = -1;
            bucketBits = 0;
            return false;
        }

        for (int lane = 0; lane < 2; lane++)
        {
            ulong chunk = candidates.AsUInt64().GetElement(lane);
            if (chunk == 0)
            {
                continue;
            }

            int bit = BitOperations.TrailingZeroCount(chunk);
            int byteOffset = bit / BucketCount;
            candidateStart = baseOffset + lane * 8 + byteOffset;
            bucketBits = (byte)(chunk >> (byteOffset * BucketCount));
            return true;
        }

        candidateStart = -1;
        bucketBits = 0;
        return false;
    }

    private bool TryVerifyAt(
        ReadOnlySpan<byte> haystack,
        int start,
        byte bucketBits,
        out RegexLiteralSetCandidate candidate)
    {
        if (commonFoldedLiterals is null && singleLiteralBuckets is not null)
        {
            return TryVerifySingleLiteralBucketsAt(haystack, start, bucketBits, out candidate);
        }

        RegexLiteralSetCandidate? best = null;
        uint remainingBuckets = bucketBits;
        while (remainingBuckets != 0)
        {
            int bucket = BitOperations.TrailingZeroCount(remainingBuckets);
            remainingBuckets &= remainingBuckets - 1;
            int[] literalIds = buckets[bucket];
            for (int index = 0; index < literalIds.Length; index++)
            {
                int literalId = literalIds[index];
                int length;
                if (commonFoldedLiterals is null)
                {
                    byte[] literal = literals[literalId];
                    if (literal.Length > haystack.Length - start ||
                        haystack[start + literal.Length - 1] != literal[^1] ||
                        !haystack.Slice(start, literal.Length).SequenceEqual(literal))
                    {
                        continue;
                    }

                    length = literal.Length;
                }
                else if (!TryMatchCommonFoldedLiteralAt(haystack[start..], commonFoldedLiterals[literalId], out length))
                {
                    continue;
                }

                var current = new RegexLiteralSetCandidate(literalId, new RegexMatch(start, length));
                if (IsBetter(current, best))
                {
                    best = current;
                }
            }
        }

        candidate = best.GetValueOrDefault();
        return best.HasValue;
    }

    private bool TryVerifySingleLiteralBucketsAt(
        ReadOnlySpan<byte> haystack,
        int start,
        byte bucketBits,
        out RegexLiteralSetCandidate candidate)
    {
        RegexLiteralSetCandidate? best = null;
        uint remainingBuckets = bucketBits;
        while (remainingBuckets != 0)
        {
            int bucket = BitOperations.TrailingZeroCount(remainingBuckets);
            remainingBuckets &= remainingBuckets - 1;
            int literalId = singleLiteralBuckets![bucket];
            if (literalId < 0)
            {
                continue;
            }

            byte[] literal = literals[literalId];
            if (literal.Length > haystack.Length - start ||
                haystack[start + literal.Length - 1] != literal[^1] ||
                !haystack.Slice(start, literal.Length).SequenceEqual(literal))
            {
                continue;
            }

            var current = new RegexLiteralSetCandidate(literalId, new RegexMatch(start, literal.Length));
            if (IsBetter(current, best))
            {
                best = current;
            }
        }

        candidate = best.GetValueOrDefault();
        return best.HasValue;
    }

    private byte ExactPrefixBucketBits(ReadOnlySpan<byte> haystack)
    {
        byte bits = 0;
        for (int bucket = 0; bucket < buckets.Length; bucket++)
        {
            int[] literalIds = buckets[bucket];
            for (int index = 0; index < literalIds.Length; index++)
            {
                int literalId = literalIds[index];
                if (prefixByteVariants is null
                    ? haystack[..MaskLength].SequenceEqual(literals[literalId].AsSpan(anchorOffset, MaskLength))
                    : PrefixVariantsMatch(haystack, prefixByteVariants[literalId]))
                {
                    bits |= (byte)(1 << bucket);
                    break;
                }
            }
        }

        return bits;
    }

    private static bool PrefixVariantsMatch(ReadOnlySpan<byte> haystack, byte[][] variants)
    {
        for (int byteIndex = 0; byteIndex < MaskLength; byteIndex++)
        {
            byte[] byteVariants = variants[byteIndex];
            byte value = haystack[byteIndex];
            bool matched = false;
            for (int variantIndex = 0; variantIndex < byteVariants.Length; variantIndex++)
            {
                if (value == byteVariants[variantIndex])
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private void BuildMasks()
    {
        byte[] low = new byte[32];
        byte[] high = new byte[32];
        for (int byteIndex = 0; byteIndex < MaskLength; byteIndex++)
        {
            Array.Clear(low);
            Array.Clear(high);
            for (int bucket = 0; bucket < buckets.Length; bucket++)
            {
                int[] literalIds = buckets[bucket];
                for (int index = 0; index < literalIds.Length; index++)
                {
                    int literalId = literalIds[index];
                    if (prefixByteVariants is null)
                    {
                        AddMaskByte(low, high, bucket, literals[literalId][anchorOffset + byteIndex]);
                        continue;
                    }

                    byte[] variants = prefixByteVariants[literalId][byteIndex];
                    for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
                    {
                        AddMaskByte(low, high, bucket, variants[variantIndex]);
                    }
                }
            }

            ref byte lowReference = ref MemoryMarshal.GetArrayDataReference(low);
            ref byte highReference = ref MemoryMarshal.GetArrayDataReference(high);
            lowMasks128[byteIndex] = Vector128.LoadUnsafe(ref lowReference);
            highMasks128[byteIndex] = Vector128.LoadUnsafe(ref highReference);
            lowMasks256[byteIndex] = Vector256.LoadUnsafe(ref lowReference);
            highMasks256[byteIndex] = Vector256.LoadUnsafe(ref highReference);
        }
    }

    private static void AddMaskByte(Span<byte> low, Span<byte> high, int bucket, byte value)
    {
        byte bit = (byte)(1 << bucket);
        int lowNibble = value & 0x0F;
        int highNibble = value >> 4;
        low[lowNibble] |= bit;
        low[lowNibble + 16] |= bit;
        high[highNibble] |= bit;
        high[highNibble + 16] |= bit;
    }

    private static int[][] BuildBuckets(byte[][] literals, int anchorOffset)
    {
        var bucketLists = new List<int>[BucketCount];
        for (int index = 0; index < bucketLists.Length; index++)
        {
            bucketLists[index] = [];
        }

        var lowNibbleBuckets = new Dictionary<int, int>();
        for (int literalId = 0; literalId < literals.Length; literalId++)
        {
            int lowNibblePrefix = LowNibblePrefix(literals[literalId], anchorOffset);
            if (!lowNibbleBuckets.TryGetValue(lowNibblePrefix, out int bucket))
            {
                bucket = BucketCount - 1 - literalId % BucketCount;
                lowNibbleBuckets.Add(lowNibblePrefix, bucket);
            }

            bucketLists[bucket].Add(literalId);
        }

        int[][] buckets = new int[BucketCount][];
        for (int index = 0; index < buckets.Length; index++)
        {
            buckets[index] = bucketLists[index].ToArray();
        }

        return buckets;
    }

    private static int[]? TryBuildSingleLiteralBuckets(int[][] buckets)
    {
        int[] singleLiterals = new int[buckets.Length];
        Array.Fill(singleLiterals, -1);
        for (int bucket = 0; bucket < buckets.Length; bucket++)
        {
            int[] literalIds = buckets[bucket];
            if (literalIds.Length == 0)
            {
                continue;
            }

            if (literalIds.Length != 1)
            {
                return null;
            }

            singleLiterals[bucket] = literalIds[0];
        }

        return singleLiterals;
    }

    private static int SelectAnchorOffset(byte[][] literals)
    {
        int maxOffset = MinimumLength(literals) - MaskLength;
        if (maxOffset <= 0)
        {
            return 0;
        }

        if (ContainsNonAscii(literals))
        {
            return ContainsAsciiOrTwoByteUtf8Only(literals) ? maxOffset / 2 : maxOffset;
        }

        int bestOffset = 0;
        int bestScore = EstimateAsciiAnchorScore(literals, 0);
        for (int offset = 1; offset <= maxOffset; offset++)
        {
            int score = EstimateAsciiAnchorScore(literals, offset);
            if (score < bestScore)
            {
                bestOffset = offset;
                bestScore = score;
            }
        }

        return bestOffset;
    }

    private static int MinimumLength(byte[][] literals)
    {
        int minimum = int.MaxValue;
        for (int index = 0; index < literals.Length; index++)
        {
            minimum = Math.Min(minimum, literals[index].Length);
        }

        return minimum;
    }

    private static int EstimateAsciiAnchorScore(byte[][] literals, int offset)
    {
        int score = 0;
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            for (int byteIndex = 0; byteIndex < MaskLength; byteIndex++)
            {
                score += EstimatedAsciiByteFrequency(literal[offset + byteIndex]);
            }
        }

        return score;
    }

    private static int EstimatedAsciiByteFrequency(byte value)
    {
        if (value == (byte)' ')
        {
            return 40;
        }

        if (value is >= (byte)'a' and <= (byte)'z')
        {
            return 25;
        }

        if (value is >= (byte)'A' and <= (byte)'Z')
        {
            return 12;
        }

        if (value is >= (byte)'0' and <= (byte)'9')
        {
            return 16;
        }

        return value < 0x80 ? 8 : 20;
    }

    private static bool TryBuildCommonFoldedLiteral(byte[] literal, out int[] foldedLiteral)
    {
        var folded = new List<int>();
        int index = 0;
        while (index < literal.Length)
        {
            if (!TryReadCommonFoldedScalar(literal, index, out int scalar, out int consumed))
            {
                foldedLiteral = [];
                return false;
            }

            folded.Add(scalar);
            index += consumed;
        }

        foldedLiteral = folded.ToArray();
        return true;
    }

    private static bool TryBuildCommonCyrillicPrefixByteVariants(byte[] literal, out byte[][] variants)
    {
        variants = new byte[MaskLength][];
        int sourceIndex = 0;
        int prefixIndex = 0;
        while (prefixIndex < MaskLength)
        {
            if (!TryReadCommonCyrillicScalarByteVariants(
                    literal,
                    sourceIndex,
                    out byte[][] scalarVariants,
                    out int consumed))
            {
                variants = [];
                return false;
            }

            for (int scalarByteIndex = 0; scalarByteIndex < consumed && prefixIndex < MaskLength; scalarByteIndex++)
            {
                variants[prefixIndex++] = DistinctVariantBytes(scalarVariants, scalarByteIndex);
            }

            sourceIndex += consumed;
        }

        return true;
    }

    private static byte[] DistinctVariantBytes(byte[][] scalarVariants, int byteIndex)
    {
        var bytes = new List<byte>();
        for (int index = 0; index < scalarVariants.Length; index++)
        {
            byte value = scalarVariants[index][byteIndex];
            if (!bytes.Contains(value))
            {
                bytes.Add(value);
            }
        }

        return bytes.ToArray();
    }

    private static bool TryReadCommonCyrillicScalarByteVariants(
        byte[] bytes,
        int index,
        out byte[][] variants,
        out int consumed)
    {
        variants = [];
        consumed = 0;
        if ((uint)index >= (uint)bytes.Length)
        {
            return false;
        }

        byte first = bytes[index];
        if (first <= 0x7F)
        {
            if (first is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z')
            {
                return false;
            }

            variants = [[first]];
            consumed = 1;
            return true;
        }

        if (index + 1 >= bytes.Length)
        {
            return false;
        }

        byte second = bytes[index + 1];
        if (first == 0xD0)
        {
            if (second is >= 0x90 and <= 0x9F)
            {
                variants = [[0xD0, second], [0xD0, (byte)(second + 0x20)]];
                consumed = 2;
                return true;
            }

            if (second is >= 0xA0 and <= 0xAF)
            {
                variants = [[0xD0, second], [0xD1, (byte)(second - 0x20)]];
                consumed = 2;
                return true;
            }

            if (second is >= 0xB0 and <= 0xBF)
            {
                variants = [[0xD0, (byte)(second - 0x20)], [0xD0, second]];
                consumed = 2;
                return true;
            }

            if (second == 0x81)
            {
                variants = [[0xD0, 0x81], [0xD1, 0x91]];
                consumed = 2;
                return true;
            }
        }
        else if (first == 0xD1)
        {
            if (second is >= 0x80 and <= 0x8F)
            {
                variants = [[0xD0, (byte)(second + 0x20)], [0xD1, second]];
                consumed = 2;
                return true;
            }

            if (second == 0x91)
            {
                variants = [[0xD0, 0x81], [0xD1, 0x91]];
                consumed = 2;
                return true;
            }
        }

        return false;
    }

    private static bool TryMatchCommonFoldedLiteralAt(ReadOnlySpan<byte> haystack, int[] literal, out int length)
    {
        length = 0;
        int haystackIndex = 0;
        for (int literalIndex = 0; literalIndex < literal.Length; literalIndex++)
        {
            if (!TryReadCommonFoldedScalar(haystack, haystackIndex, out int haystackFolded, out int haystackConsumed) ||
                literal[literalIndex] != haystackFolded)
            {
                length = 0;
                return false;
            }

            haystackIndex += haystackConsumed;
            length += haystackConsumed;
        }

        return true;
    }

    private static bool TryReadCommonFoldedScalar(
        ReadOnlySpan<byte> bytes,
        int index,
        out int folded,
        out int consumed)
    {
        folded = 0;
        consumed = 0;
        if ((uint)index >= (uint)bytes.Length)
        {
            return false;
        }

        byte first = bytes[index];
        if (first <= 0x7F)
        {
            folded = FastSimpleFold(first);
            consumed = 1;
            return true;
        }

        if (index + 1 >= bytes.Length)
        {
            return false;
        }

        byte second = bytes[index + 1];
        if (first == 0xD0)
        {
            if (second is >= 0x90 and <= 0x9F)
            {
                folded = 0x0430 + (second - 0x90);
                consumed = 2;
                return true;
            }

            if (second is >= 0xA0 and <= 0xAF)
            {
                folded = 0x0440 + (second - 0xA0);
                consumed = 2;
                return true;
            }

            if (second is >= 0xB0 and <= 0xBF)
            {
                folded = 0x0430 + (second - 0xB0);
                consumed = 2;
                return true;
            }

            if (second == 0x81)
            {
                folded = 0x0451;
                consumed = 2;
                return true;
            }
        }
        else if (first == 0xD1)
        {
            if (second is >= 0x80 and <= 0x8F)
            {
                folded = 0x0440 + (second - 0x80);
                consumed = 2;
                return true;
            }

            if (second == 0x91)
            {
                folded = 0x0451;
                consumed = 2;
                return true;
            }
        }

        return false;
    }

    private static int FastSimpleFold(int scalar)
    {
        if ((uint)(scalar - 'A') <= 'Z' - 'A')
        {
            return scalar + 32;
        }

        if ((uint)(scalar - 0x0410) <= 0x042F - 0x0410)
        {
            return scalar + 0x20;
        }

        return scalar == 0x0401 ? 0x0451 : scalar;
    }

    private static bool ContainsNonAscii(byte[][] literals)
    {
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            for (int byteIndex = 0; byteIndex < literal.Length; byteIndex++)
            {
                if (literal[byteIndex] > 0x7F)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsAsciiOrTwoByteUtf8Only(byte[][] literals)
    {
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
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

    private static int LowNibblePrefix(byte[] literal, int offset)
    {
        return (literal[offset] & 0x0F) |
            ((literal[offset + 1] & 0x0F) << 4) |
            ((literal[offset + 2] & 0x0F) << 8) |
            ((literal[offset + 3] & 0x0F) << 12);
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
}
