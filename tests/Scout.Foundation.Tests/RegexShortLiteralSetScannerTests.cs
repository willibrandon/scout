namespace Scout;

/// <summary>
/// Verifies exact short literal-set scanning across vector and scalar boundaries.
/// </summary>
public sealed class RegexShortLiteralSetScannerTests
{
    /// <summary>
    /// Verifies the scanner rejects inputs with no exact literal match.
    /// </summary>
    [Fact]
    public void NoMatchingLiteralReturnsNoCandidateOrCount()
    {
        RegexShortLiteralSetScanner scanner = CreateScanner(
            "Generated"u8.ToArray(),
            "PaladinRecord"u8.ToArray(),
            "PaladinValue"u8.ToArray());
        byte[] haystack = Enumerable.Repeat((byte)'x', 128).ToArray();

        Assert.Null(scanner.Find(haystack, startAt: 0));
        Assert.Equal(0, scanner.CountOrSum(haystack, startAt: 0, sumSpans: false));
        Assert.Equal(0, scanner.CountOrSum(haystack, startAt: 0, sumSpans: true));
    }

    /// <summary>
    /// Verifies regex preference order controls same-position matches and subsequent overlap traversal.
    /// </summary>
    [Fact]
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

        Assert.Equal(new RegexMatch(0, 4), longerFirst.Find(haystack, startAt: 0)?.Match);
        Assert.Equal(new RegexMatch(0, 3), shorterFirst.Find(haystack, startAt: 0)?.Match);
        Assert.Equal(1, longerFirst.CountOrSum(haystack, startAt: 0, sumSpans: false));
        Assert.Equal(4, longerFirst.CountOrSum(haystack, startAt: 0, sumSpans: true));
        Assert.Equal(2, shorterFirst.CountOrSum(haystack, startAt: 0, sumSpans: false));
        Assert.Equal(6, shorterFirst.CountOrSum(haystack, startAt: 0, sumSpans: true));
    }

    /// <summary>
    /// Verifies matches beginning around 128-bit and 256-bit vector boundaries remain visible.
    /// </summary>
    /// <param name="matchOffset">The byte offset at which the literal begins.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(62)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
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

        Assert.True(candidate.HasValue);
        Assert.Equal(1, candidate.Value.LiteralId);
        Assert.Equal(new RegexMatch(matchOffset, literal.Length), candidate.Value.Match);
        Assert.Equal(1, scanner.CountOrSum(haystack, StartAt, sumSpans: false));
        Assert.Equal(literal.Length, scanner.CountOrSum(haystack, StartAt, sumSpans: true));
        Assert.Null(scanner.Find(haystack, matchOffset + 1));
        Assert.Equal(0, scanner.CountOrSum(haystack, matchOffset + 1, sumSpans: false));
    }

    /// <summary>
    /// Verifies a nonzero search offset preserves a match in a later vector iteration.
    /// </summary>
    [Fact]
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

        Assert.True(candidate.HasValue);
        Assert.Equal(2, candidate.Value.LiteralId);
        Assert.Equal(new RegexMatch(MatchOffset, literal.Length), candidate.Value.Match);
        Assert.Equal(1, scanner.CountOrSum(haystack, StartAt, sumSpans: false));
        Assert.Equal(literal.Length, scanner.CountOrSum(haystack, StartAt, sumSpans: true));
    }

    /// <summary>
    /// Verifies nibble-bucket collisions are confirmed against complete literals before reporting a match.
    /// </summary>
    [Fact]
    public void BucketCollisionsRequireExactLiteralVerification()
    {
        RegexShortLiteralSetScanner scanner = CreateScanner(
            "Abc-one"u8.ToArray(),
            "Qrs-two"u8.ToArray(),
            "xyz"u8.ToArray());
        byte[] haystack = "Arc-false Abc-one Qrs-two"u8.ToArray();

        RegexLiteralSetCandidate? candidate = scanner.Find(haystack, startAt: 0);

        Assert.True(candidate.HasValue);
        Assert.Equal(0, candidate.Value.LiteralId);
        Assert.Equal(new RegexMatch(10, 7), candidate.Value.Match);
        Assert.Equal(2, scanner.CountOrSum(haystack, startAt: 0, sumSpans: false));
        Assert.Equal(14, scanner.CountOrSum(haystack, startAt: 0, sumSpans: true));
    }

    private static RegexShortLiteralSetScanner CreateScanner(params byte[][] literals)
    {
        Assert.True(RegexShortLiteralSetScanner.TryCreate(literals, out RegexShortLiteralSetScanner? scanner));
        Assert.NotNull(scanner);
        return scanner;
    }
}
