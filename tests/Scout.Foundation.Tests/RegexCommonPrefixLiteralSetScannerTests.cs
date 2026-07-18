using System.Text;

namespace Scout;

/// <summary>
/// Verifies exact common-prefix literal-set scanning semantics and candidate indexing.
/// </summary>
public sealed class RegexCommonPrefixLiteralSetScannerTests
{
    /// <summary>
    /// Verifies frequent false prefix occurrences select only the source-ordered suffix bucket.
    /// </summary>
    [Fact]
    public void FrequentFalsePrefixesUseTheMatchingSuffixBucket()
    {
        byte[][] literals = Enumerable.Range(0, 64)
            .Select(static index => Encoding.ASCII.GetBytes($"issue44_absent_pattern_{index:D3}"))
            .ToArray();
        bool created = RegexCommonPrefixLiteralSetScanner.TryCreate(
            literals,
            out RegexCommonPrefixLiteralSetScanner? scanner);
        byte[] falseCandidates = Encoding.ASCII.GetBytes(
            string.Concat(Enumerable.Repeat("issue44_absent_pattern_099\n", 4_096)));

        Assert.True(created);
        Assert.NotNull(scanner);
        Assert.Equal(0, scanner.GetVerificationCandidateCount((byte)'9'));
        Assert.Equal(4, scanner.GetVerificationCandidateCount((byte)'6'));
        Assert.Null(scanner.Find(falseCandidates, startAt: 0));
        Assert.Equal(0, scanner.CountMatches(falseCandidates, startAt: 0));
        Assert.Equal(0, scanner.SumMatchSpans(falseCandidates, startAt: 0));
    }

    /// <summary>
    /// Verifies literals equal to the common prefix merge with continuing literals in source order.
    /// </summary>
    [Fact]
    public void ExactPrefixLiteralsPreserveSourceOrder()
    {
        byte[][] longerFirst = CreateExactPrefixLiterals(prefixFirst: false);
        byte[][] prefixFirst = CreateExactPrefixLiterals(prefixFirst: true);
        Assert.True(RegexCommonPrefixLiteralSetScanner.TryCreate(
            longerFirst,
            out RegexCommonPrefixLiteralSetScanner? longerScanner));
        Assert.True(RegexCommonPrefixLiteralSetScanner.TryCreate(
            prefixFirst,
            out RegexCommonPrefixLiteralSetScanner? prefixScanner));
        byte[] haystack = "aaaaaaaaZ"u8.ToArray();

        Assert.NotNull(longerScanner);
        Assert.NotNull(prefixScanner);
        Assert.Equal(2, longerScanner.GetVerificationCandidateCount((byte)'Z'));
        Assert.Equal(2, prefixScanner.GetVerificationCandidateCount((byte)'Z'));
        Assert.Equal(new RegexMatch(0, 9), longerScanner.Find(haystack, startAt: 0)?.Match);
        Assert.Equal(new RegexMatch(0, 8), prefixScanner.Find(haystack, startAt: 0)?.Match);
        Assert.Equal(9, longerScanner.SumMatchSpans(haystack, startAt: 0));
        Assert.Equal(8, prefixScanner.SumMatchSpans(haystack, startAt: 0));
    }

    /// <summary>
    /// Verifies fused counting preserves source order when the selected overlap changes the
    /// subsequent non-overlapping match count.
    /// </summary>
    [Fact]
    public void FusedCountingPreservesSourceOrderedOverlaps()
    {
        byte[][] longerFirst = CreateOverlappingCountLiterals(shorterFirst: false);
        byte[][] shorterFirst = CreateOverlappingCountLiterals(shorterFirst: true);
        Assert.True(RegexCommonPrefixLiteralSetScanner.TryCreate(
            longerFirst,
            out RegexCommonPrefixLiteralSetScanner? longerScanner));
        Assert.True(RegexCommonPrefixLiteralSetScanner.TryCreate(
            shorterFirst,
            out RegexCommonPrefixLiteralSetScanner? shorterScanner));
        byte[] haystack = "aaaaaaaaaaaaaaaa\0"u8.ToArray();

        Assert.NotNull(longerScanner);
        Assert.NotNull(shorterScanner);
        AssertFusedCount(longerScanner, haystack, expectedCount: 1);
        AssertFusedCount(shorterScanner, haystack, expectedCount: 2);
    }

    /// <summary>
    /// Verifies NUL detection remains complete when rejected common-prefix candidates overlap.
    /// </summary>
    [Fact]
    public void FusedCountingDetectsNulAroundRejectedOverlappingPrefixes()
    {
        byte[][] literals = Enumerable.Range(0, 16)
            .Select(static index => Encoding.ASCII.GetBytes($"aaaaaaaaX{index:X2}"))
            .ToArray();
        Assert.True(RegexCommonPrefixLiteralSetScanner.TryCreate(
            literals,
            out RegexCommonPrefixLiteralSetScanner? scanner));
        Assert.NotNull(scanner);
        byte[] overlappingCandidate = "aaaaaaaaaX00"u8.ToArray();

        AssertFusedCount(scanner, overlappingCandidate, expectedCount: 1);
        for (int nulOffset = 0; nulOffset <= overlappingCandidate.Length; nulOffset++)
        {
            byte[] haystack = new byte[overlappingCandidate.Length + 1];
            overlappingCandidate.AsSpan(0, nulOffset).CopyTo(haystack);
            overlappingCandidate.AsSpan(nulOffset).CopyTo(haystack.AsSpan(nulOffset + 1));
            AssertFusedCount(scanner, haystack);
        }
    }

    /// <summary>
    /// Verifies common-prefix counting observes NUL bytes before, within, between, and after
    /// candidates without changing source-ordered non-overlapping counts.
    /// </summary>
    [Fact]
    public void CountMatchesDetectsNulAcrossCandidateTraversal()
    {
        byte[][] literals = Enumerable.Range(0, 64)
            .Select(static index =>
                Encoding.ASCII.GetBytes($"issue44_absent_pattern_{index:D3}"))
            .ToArray();
        Assert.True(RegexCommonPrefixLiteralSetScanner.TryCreate(
            literals,
            out RegexCommonPrefixLiteralSetScanner? scanner));
        Assert.NotNull(scanner);

        byte[][] haystacks =
        [
            "ordinary source text"u8.ToArray(),
            "\0ordinary source text"u8.ToArray(),
            "issue44_absent_pattern_099\0"u8.ToArray(),
            "issue44_absent_pattern_001\0issue44_absent_pattern_002"u8.ToArray(),
            "issue44_absent_pattern_003 trailing\0"u8.ToArray(),
        ];
        for (int index = 0; index < haystacks.Length; index++)
        {
            AssertFusedCount(scanner, haystacks[index]);
        }

        byte[][] prefixNulLiterals = Enumerable.Range(0, 16)
            .Select(static index => Encoding.ASCII.GetBytes($"prefix\0Q{index:X2}"))
            .ToArray();
        Assert.True(RegexCommonPrefixLiteralSetScanner.TryCreate(
            prefixNulLiterals,
            out RegexCommonPrefixLiteralSetScanner? prefixNulScanner));
        Assert.NotNull(prefixNulScanner);
        AssertFusedCount(prefixNulScanner, "prefix\0Q00"u8.ToArray());

        byte[][] suffixNulLiterals = Enumerable.Range(0, 16)
            .Select(static index => Encoding.ASCII.GetBytes($"abcdefgh{index:X2}"))
            .ToArray();
        suffixNulLiterals[0] = "abcdefgh00\0tail"u8.ToArray();
        Assert.True(RegexCommonPrefixLiteralSetScanner.TryCreate(
            suffixNulLiterals,
            out RegexCommonPrefixLiteralSetScanner? suffixNulScanner));
        Assert.NotNull(suffixNulScanner);
        AssertFusedCount(suffixNulScanner, "abcdefgh00\0tail"u8.ToArray());
    }

    private static void AssertFusedCount(
        RegexCommonPrefixLiteralSetScanner scanner,
        byte[] haystack,
        long? expectedCount = null)
    {
        Assert.True(scanner.TryCountMatchesAndDetectNul(
            haystack,
            out long count,
            out bool containsNul));
        Assert.Equal(scanner.CountMatches(haystack, startAt: 0), count);
        if (expectedCount.HasValue)
        {
            Assert.Equal(expectedCount.Value, count);
        }

        Assert.Equal(haystack.AsSpan().Contains((byte)0), containsNul);
    }

    private static byte[][] CreateOverlappingCountLiterals(bool shorterFirst)
    {
        byte[][] literals = Enumerable.Range(0, 16)
            .Select(static index => Encoding.ASCII.GetBytes($"aaaaaaaaZ{index:X2}"))
            .ToArray();
        literals[0] = shorterFirst ? "aaaaaaaa"u8.ToArray() : "aaaaaaaaa"u8.ToArray();
        literals[1] = shorterFirst ? "aaaaaaaaa"u8.ToArray() : "aaaaaaaa"u8.ToArray();
        return literals;
    }

    private static byte[][] CreateExactPrefixLiterals(bool prefixFirst)
    {
        byte[][] literals = Enumerable.Range(0, 16)
            .Select(static index => Encoding.ASCII.GetBytes($"aaaaaaaa{(char)('A' + index)}"))
            .ToArray();
        literals[0] = prefixFirst ? "aaaaaaaa"u8.ToArray() : "aaaaaaaaZ"u8.ToArray();
        literals[1] = prefixFirst ? "aaaaaaaaZ"u8.ToArray() : "aaaaaaaa"u8.ToArray();
        return literals;
    }
}
