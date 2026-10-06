namespace Scout;

/// <summary>
/// Verifies exact short literal-set scanning across vector and scalar boundaries.
/// </summary>
[TestClass]
public sealed class RegexShortLiteralSetScannerTests
{
    /// <summary>
    /// Verifies the scanner rejects inputs with no exact literal match.
    /// </summary>
    [TestMethod]
    public void NoMatchingLiteralReturnsNoCandidateOrCount()
    {
        RegexShortLiteralSetScanner scanner = CreateScanner(
            "Generated"u8.ToArray(),
            "PaladinRecord"u8.ToArray(),
            "PaladinValue"u8.ToArray());
        byte[] haystack = Enumerable.Repeat((byte)'x', 128).ToArray();

        Assert.IsNull(scanner.Find(haystack, startAt: 0));
        Assert.AreEqual(0, scanner.CountOrSum(haystack, startAt: 0, sumSpans: false));
        Assert.AreEqual(0, scanner.CountOrSum(haystack, startAt: 0, sumSpans: true));
    }

    /// <summary>
    /// Verifies regex preference order controls same-position matches and subsequent overlap traversal.
    /// </summary>
    [TestMethod]
    public void SourceOrderControlsOverlappingMatches()
    {
        RegexShortLiteralSetScanner longerFirst = CreateScanner(
            "aaaa"u8.ToArray(),
            "aaa"u8.ToArray(),
            "bbb"u8.ToArray());
        RegexShortLiteralSetScanner shorterFirst = CreateScanner(
            "aaa"u8.ToArray(),
            "aaaa"u8.ToArray(),
            "bbb"u8.ToArray());
        byte[] haystack = "aaaaaa"u8.ToArray();

        RegexLiteralSetCandidate? longerFirstMatch = longerFirst.Find(haystack, startAt: 0);
        Assert.IsTrue(longerFirstMatch.HasValue);
        Assert.AreEqual(new RegexMatch(0, 4), longerFirstMatch.Value.Match);
        RegexLiteralSetCandidate? shorterFirstMatch = shorterFirst.Find(haystack, startAt: 0);
        Assert.IsTrue(shorterFirstMatch.HasValue);
        Assert.AreEqual(new RegexMatch(0, 3), shorterFirstMatch.Value.Match);
        Assert.AreEqual(1, longerFirst.CountOrSum(haystack, startAt: 0, sumSpans: false));
        Assert.AreEqual(4, longerFirst.CountOrSum(haystack, startAt: 0, sumSpans: true));
        Assert.AreEqual(2, shorterFirst.CountOrSum(haystack, startAt: 0, sumSpans: false));
        Assert.AreEqual(6, shorterFirst.CountOrSum(haystack, startAt: 0, sumSpans: true));
    }

    /// <summary>
    /// Verifies matches beginning around 128-bit and 256-bit vector boundaries remain visible.
    /// </summary>
    /// <param name="matchOffset">The byte offset at which the literal begins.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(14)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(30)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(62)]
    [DataRow(63)]
    [DataRow(64)]
    [DataRow(65)]
    public void VectorBoundariesAndStartOffsetsPreserveMatches(int matchOffset)
    {
        RegexShortLiteralSetScanner scanner = CreateScanner(
            "Generated"u8.ToArray(),
            "PaladinRecord"u8.ToArray(),
            "PaladinValue"u8.ToArray());
        byte[] literal = "PaladinRecord"u8.ToArray();
        byte[] haystack = Enumerable.Repeat((byte)'_', matchOffset + literal.Length + 64).ToArray();
        literal.CopyTo(haystack, matchOffset);
        const int StartAt = 0;

        RegexLiteralSetCandidate? candidate = scanner.Find(haystack, StartAt);

        Assert.IsTrue(candidate.HasValue);
        Assert.AreEqual(1, candidate.Value.LiteralId);
        Assert.AreEqual(new RegexMatch(matchOffset, literal.Length), candidate.Value.Match);
        Assert.AreEqual(1, scanner.CountOrSum(haystack, StartAt, sumSpans: false));
        Assert.AreEqual(literal.Length, scanner.CountOrSum(haystack, StartAt, sumSpans: true));
        Assert.IsNull(scanner.Find(haystack, matchOffset + 1));
        Assert.AreEqual(0, scanner.CountOrSum(haystack, matchOffset + 1, sumSpans: false));
    }

    /// <summary>
    /// Verifies a nonzero search offset preserves a match in a later vector iteration.
    /// </summary>
    [TestMethod]
    public void NonzeroStartOffsetPreservesLaterVectorMatch()
    {
        const int MatchOffset = 31;
        const int StartAt = 7;
        RegexShortLiteralSetScanner scanner = CreateScanner(
            "Generated"u8.ToArray(),
            "PaladinRecord"u8.ToArray(),
            "PaladinValue"u8.ToArray());
        byte[] literal = "PaladinValue"u8.ToArray();
        byte[] haystack = Enumerable.Repeat((byte)'_', 96).ToArray();
        literal.CopyTo(haystack, MatchOffset);

        RegexLiteralSetCandidate? candidate = scanner.Find(haystack, StartAt);

        Assert.IsTrue(candidate.HasValue);
        Assert.AreEqual(2, candidate.Value.LiteralId);
        Assert.AreEqual(new RegexMatch(MatchOffset, literal.Length), candidate.Value.Match);
        Assert.AreEqual(1, scanner.CountOrSum(haystack, StartAt, sumSpans: false));
        Assert.AreEqual(literal.Length, scanner.CountOrSum(haystack, StartAt, sumSpans: true));
    }

    /// <summary>
    /// Verifies nibble-bucket collisions are confirmed against complete literals before reporting a match.
    /// </summary>
    [TestMethod]
    public void BucketCollisionsRequireExactLiteralVerification()
    {
        RegexShortLiteralSetScanner scanner = CreateScanner(
            "Abc-one"u8.ToArray(),
            "Qrs-two"u8.ToArray(),
            "xyz"u8.ToArray());
        byte[] haystack = "Arc-false Abc-one Qrs-two"u8.ToArray();

        RegexLiteralSetCandidate? candidate = scanner.Find(haystack, startAt: 0);

        Assert.IsTrue(candidate.HasValue);
        Assert.AreEqual(0, candidate.Value.LiteralId);
        Assert.AreEqual(new RegexMatch(10, 7), candidate.Value.Match);
        Assert.AreEqual(2, scanner.CountOrSum(haystack, startAt: 0, sumSpans: false));
        Assert.AreEqual(14, scanner.CountOrSum(haystack, startAt: 0, sumSpans: true));
    }

    private static RegexShortLiteralSetScanner CreateScanner(params byte[][] literals)
    {
        Assert.IsTrue(RegexShortLiteralSetScanner.TryCreate(literals, out RegexShortLiteralSetScanner? scanner));
        Assert.IsNotNull(scanner);
        return scanner;
    }
}
