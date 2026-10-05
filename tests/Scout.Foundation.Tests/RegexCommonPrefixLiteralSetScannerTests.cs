using System.Text;

namespace Scout;

/// <summary>
/// Verifies exact common-prefix literal-set scanning semantics and candidate indexing.
/// </summary>
[TestClass]
public sealed class RegexCommonPrefixLiteralSetScannerTests
{
    /// <summary>
    /// Verifies frequent false prefix occurrences select only the source-ordered suffix bucket.
    /// </summary>
    [TestMethod]
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

        Assert.IsTrue(created);
        Assert.IsNotNull(scanner);
        Assert.AreEqual(0, scanner.GetVerificationCandidateCount((byte)'9'));
        Assert.AreEqual(4, scanner.GetVerificationCandidateCount((byte)'6'));
        Assert.IsNull(scanner.Find(falseCandidates, startAt: 0));
        Assert.AreEqual(0, scanner.CountMatches(falseCandidates, startAt: 0));
        Assert.AreEqual(0, scanner.SumMatchSpans(falseCandidates, startAt: 0));
    }

    /// <summary>
    /// Verifies literals equal to the common prefix merge with continuing literals in source order.
    /// </summary>
    [TestMethod]
    public void ExactPrefixLiteralsPreserveSourceOrder()
    {
        byte[][] longerFirst = CreateExactPrefixLiterals(prefixFirst: false);
        byte[][] prefixFirst = CreateExactPrefixLiterals(prefixFirst: true);
        Assert.IsTrue(RegexCommonPrefixLiteralSetScanner.TryCreate(
            longerFirst,
            out RegexCommonPrefixLiteralSetScanner? longerScanner));
        Assert.IsTrue(RegexCommonPrefixLiteralSetScanner.TryCreate(
            prefixFirst,
            out RegexCommonPrefixLiteralSetScanner? prefixScanner));
        byte[] haystack = "aaaaaaaaZ"u8.ToArray();

        Assert.IsNotNull(longerScanner);
        Assert.IsNotNull(prefixScanner);
        Assert.AreEqual(2, longerScanner.GetVerificationCandidateCount((byte)'Z'));
        Assert.AreEqual(2, prefixScanner.GetVerificationCandidateCount((byte)'Z'));
        RegexLiteralSetCandidate? longerScannerMatch = longerScanner.Find(haystack, startAt: 0);
        Assert.IsTrue(longerScannerMatch.HasValue);
        Assert.AreEqual(new RegexMatch(0, 9), longerScannerMatch.Value.Match);
        RegexLiteralSetCandidate? prefixScannerMatch = prefixScanner.Find(haystack, startAt: 0);
        Assert.IsTrue(prefixScannerMatch.HasValue);
        Assert.AreEqual(new RegexMatch(0, 8), prefixScannerMatch.Value.Match);
        Assert.AreEqual(9, longerScanner.SumMatchSpans(haystack, startAt: 0));
        Assert.AreEqual(8, prefixScanner.SumMatchSpans(haystack, startAt: 0));
    }

    /// <summary>
    /// Verifies fused counting preserves source order when the selected overlap changes the
    /// subsequent non-overlapping match count.
    /// </summary>
    [TestMethod]
    public void FusedCountingPreservesSourceOrderedOverlaps()
    {
        byte[][] longerFirst = CreateOverlappingCountLiterals(shorterFirst: false);
        byte[][] shorterFirst = CreateOverlappingCountLiterals(shorterFirst: true);
        Assert.IsTrue(RegexCommonPrefixLiteralSetScanner.TryCreate(
            longerFirst,
            out RegexCommonPrefixLiteralSetScanner? longerScanner));
        Assert.IsTrue(RegexCommonPrefixLiteralSetScanner.TryCreate(
            shorterFirst,
            out RegexCommonPrefixLiteralSetScanner? shorterScanner));
        byte[] haystack = "aaaaaaaaaaaaaaaa\0"u8.ToArray();

        Assert.IsNotNull(longerScanner);
        Assert.IsNotNull(shorterScanner);
        AssertFusedCount(longerScanner, haystack, expectedCount: 1);
        AssertFusedCount(shorterScanner, haystack, expectedCount: 2);
    }

    /// <summary>
    /// Verifies NUL detection remains complete when rejected common-prefix candidates overlap.
    /// </summary>
    [TestMethod]
    public void FusedCountingDetectsNulAroundRejectedOverlappingPrefixes()
    {
        byte[][] literals = Enumerable.Range(0, 16)
            .Select(static index => Encoding.ASCII.GetBytes($"aaaaaaaaX{index:X2}"))
            .ToArray();
        Assert.IsTrue(RegexCommonPrefixLiteralSetScanner.TryCreate(
            literals,
            out RegexCommonPrefixLiteralSetScanner? scanner));
        Assert.IsNotNull(scanner);
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
    [TestMethod]
    public void CountMatchesDetectsNulAcrossCandidateTraversal()
    {
        byte[][] literals = Enumerable.Range(0, 64)
            .Select(static index =>
                Encoding.ASCII.GetBytes($"issue44_absent_pattern_{index:D3}"))
            .ToArray();
        Assert.IsTrue(RegexCommonPrefixLiteralSetScanner.TryCreate(
            literals,
            out RegexCommonPrefixLiteralSetScanner? scanner));
        Assert.IsNotNull(scanner);

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
        Assert.IsTrue(RegexCommonPrefixLiteralSetScanner.TryCreate(
            prefixNulLiterals,
            out RegexCommonPrefixLiteralSetScanner? prefixNulScanner));
        Assert.IsNotNull(prefixNulScanner);
        AssertFusedCount(prefixNulScanner, "prefix\0Q00"u8.ToArray());

        byte[][] suffixNulLiterals = Enumerable.Range(0, 16)
            .Select(static index => Encoding.ASCII.GetBytes($"abcdefgh{index:X2}"))
            .ToArray();
        suffixNulLiterals[0] = "abcdefgh00\0tail"u8.ToArray();
        Assert.IsTrue(RegexCommonPrefixLiteralSetScanner.TryCreate(
            suffixNulLiterals,
            out RegexCommonPrefixLiteralSetScanner? suffixNulScanner));
        Assert.IsNotNull(suffixNulScanner);
        AssertFusedCount(suffixNulScanner, "abcdefgh00\0tail"u8.ToArray());
    }

    private static void AssertFusedCount(
        RegexCommonPrefixLiteralSetScanner scanner,
        byte[] haystack,
        long? expectedCount = null)
    {
        Assert.IsTrue(scanner.TryCountMatchesAndDetectNul(
            haystack,
            out long count,
            out bool containsNul));
        Assert.AreEqual(scanner.CountMatches(haystack, startAt: 0), count);
        if (expectedCount.HasValue)
        {
            Assert.AreEqual(expectedCount.Value, count);
        }

        Assert.AreEqual(haystack.AsSpan().Contains((byte)0), containsNul);
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
