using System.Buffers;
using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies malformed UTF-8 can opt into replacement-scalar matching without losing byte offsets.
/// </summary>
/// <param name="testContext">The context for the current test.</param>
[TestClass]
public sealed class ByteRegexInvalidUtf8Tests(TestContext testContext)
{
    private const int ConcurrentIterations = 32;
    private const int OracleInputCount = 24;
    private const int OracleInputLength = 97;
    private const int LargeAllocationInputLength = 2 * 1024 * 1024;
    private const int InvalidByteStride = 4096;
    private const int Issue58RegressionInputLength = 1024 * 1024;
    private const long AllocationScalingSlack = 64 * 1024;
    private const string Issue58Pattern =
        @"(?u:\b|\B)(?-u:\b)ey[a-zA-Z0-9]{17,}\.ey[a-zA-Z0-9/\\_-]{17,}\.(?:[a-zA-Z0-9/\\_-]{10,}={0,2})?";

    /// <summary>
    /// Verifies replacement matching is opt-in and the existing default still ignores malformed bytes.
    /// </summary>
    [TestMethod]
    public void MatchInvalidUtf8DefaultsToFalseAndPreservesDefaultMatching()
    {
        var options = new ByteRegexOptions();
        byte[] input = [0xFF, 0xEF, 0xBF, 0xBD];

        var regex = ByteRegex.Compile(@"\u{FFFD}", options);

        Assert.IsFalse(options.MatchInvalidUtf8);
        Assert.IsTrue(regex.IsMatch(input));
        Assert.AreEqual(new ByteRegexMatch(1, 3), regex.Find(input));
        Assert.AreEqual(1, regex.Count(input));
    }

    /// <summary>
    /// Verifies representative malformed forms expose every original byte as an individual replacement scalar.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void EveryByteOfMalformedAndTruncatedUtf8MatchesAsReplacement(ByteRegexEngineMode engineMode)
    {
        byte[][] malformedInputs =
        [
            [0x80],
            [0xC0, 0xAF],
            [0xC3],
            [0xE2, 0x82],
            [0xED, 0xA0, 0x80],
            [0xF4, 0x90, 0x80, 0x80],
            [0xF5, 0x80, 0x80, 0x80],
        ];
        var regex = ByteRegex.Compile(@"(?:\u{FFFD})+", CreateOptions(engineMode));

        foreach (byte[] input in malformedInputs)
        {
            Assert.AreEqual(new ByteRegexMatch(0, input.Length), regex.Find(input));
            Assert.AreEqual(1, regex.Count(input));
        }
    }

    /// <summary>
    /// Verifies dot consumes each malformed byte but continues to exclude the configured newline unless dot-all is enabled.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void DotMatchesInvalidBytesButStillHonorsNewlineConfiguration(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, (byte)'\n', 0xE2, 0x82];
        var dot = ByteRegex.Compile(".", CreateOptions(engineMode));
        var dotAll = ByteRegex.Compile(
            ".",
            CreateOptions(engineMode, dotMatchesNewline: true));

        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(2, 1), new ByteRegexMatch(3, 1)],
            CollectMatches(dot, input));
        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(1, 1), new ByteRegexMatch(2, 1), new ByteRegexMatch(3, 1)],
            CollectMatches(dotAll, input));
    }

    /// <summary>
    /// Verifies scalar literals and classes match malformed bytes as U+FFFD while retaining the width of valid U+FFFD.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void ScalarSyntaxMatchesInvalidBytesAndEncodedReplacementCharacter(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xEF, 0xBF, 0xBD];
        string[] patterns = [@"\u{FFFD}", @"[\u{FFFD}]", @"\p{So}", "[^a]"];

        foreach (ByteRegex regex in patterns.Select(pattern => ByteRegex.Compile(pattern, CreateOptions(engineMode))))
        {
            Assert.AreEqual(new ByteRegexMatch(0, 1), regex.Find(input));
            Assert.AreEqual(new ByteRegexMatch(1, 3), regex.Find(input, startAt: 1));
            Assert.AreEqual(2, regex.Count(input));
        }
    }

    /// <summary>
    /// Verifies a scalar escape in a class does not broaden its ASCII membership.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void ScalarClassKeepsUnlistedAsciiOutsideMalformedRuns(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, (byte)'F', 0xEF, 0xBF, 0xBD];
        var regex = ByteRegex.Compile(@"[\u{FFFD}a-z]+", CreateOptions(engineMode));

        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(2, 3)],
            CollectMatches(regex, input));
    }

    /// <summary>
    /// Verifies invalid bytes have the Unicode word-boundary behavior of the non-word U+FFFD scalar.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void UnicodeWordBoundariesTreatInvalidBytesAsReplacementScalars(ByteRegexEngineMode engineMode)
    {
        byte[] input = [(byte)'a', 0xFF, (byte)'b'];
        var boundary = ByteRegex.Compile(@"\b", CreateOptions(engineMode));
        var word = ByteRegex.Compile(@"\w+", CreateOptions(engineMode));
        var invalidPair = ByteRegex.Compile(@"\u{FFFD}\B\u{FFFD}", CreateOptions(engineMode));

        Assert.AreSequenceEqual<ByteRegexMatch>(
            [
                new ByteRegexMatch(0, 0),
                new ByteRegexMatch(1, 0),
                new ByteRegexMatch(2, 0),
                new ByteRegexMatch(3, 0),
            ],
            CollectMatches(boundary, input));
        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(2, 1)],
            CollectMatches(word, input));
        Assert.AreEqual(
            new ByteRegexMatch(0, 2),
            invalidPair.Find(new byte[] { 0xFF, 0xC3 }));
    }

    /// <summary>
    /// Verifies every Unicode word predicate evaluates malformed context without scalar consumption.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void UnicodeWordPredicatesUseInPlaceMalformedContext(ByteRegexEngineMode engineMode)
    {
        byte[] mixed = [(byte)'a', 0xFF, (byte)'b'];
        ByteRegexOptions options = CreateOptions(engineMode);

        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 0), new ByteRegexMatch(2, 0)],
            CollectMatches(ByteRegex.Compile(@"\b{start}", options), mixed));
        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(1, 0), new ByteRegexMatch(3, 0)],
            CollectMatches(ByteRegex.Compile(@"\b{end}", options), mixed));
        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 0), new ByteRegexMatch(2, 0)],
            CollectMatches(ByteRegex.Compile(@"\b{start-half}", options), mixed));
        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(1, 0), new ByteRegexMatch(3, 0)],
            CollectMatches(ByteRegex.Compile(@"\b{end-half}", options), mixed));
        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 0), new ByteRegexMatch(1, 0), new ByteRegexMatch(2, 0)],
            CollectMatches(ByteRegex.Compile(@"\B", options), [0xFF, 0xC3]));
    }

    /// <summary>
    /// Verifies boundary alternation keeps observable capture participation on malformed input.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void BoundaryAlternationPreservesCapturesOnMalformedInput(ByteRegexEngineMode engineMode)
    {
        var regex = ByteRegex.Compile(@"(?u:(\b)|(\B))(?-u:.)", CreateOptions(engineMode));

        ByteRegexCaptures? boundary = regex.FindCaptures("a"u8);
        ByteRegexCaptures? nonBoundary = regex.FindCaptures([0xFF]);

        Assert.IsNotNull(boundary);
        Assert.AreEqual(new ByteRegexMatch(0, 1), boundary.Match);
        Assert.AreEqual(new ByteRegexMatch(0, 0), boundary.GetGroup(1));
        Assert.IsNull(boundary.GetGroup(2));
        Assert.IsNotNull(nonBoundary);
        Assert.AreEqual(new ByteRegexMatch(0, 1), nonBoundary.Match);
        Assert.IsNull(nonBoundary.GetGroup(1));
        Assert.AreEqual(new ByteRegexMatch(0, 0), nonBoundary.GetGroup(2));
    }

    /// <summary>
    /// Verifies the reported boundary-prefixed pattern avoids false matches without losing valid matches.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void Issue58BoundaryPrefixMatchesThroughSyntheticMalformedInput(ByteRegexEngineMode engineMode)
    {
        byte[] noMatch = CreateIssue58NoMatchInput(Issue58RegressionInputLength);
        byte[] positive = CreateIssue58PositiveInput();
        var defaultRegex = ByteRegex.Compile(
            Issue58Pattern,
            new ByteRegexOptions { EngineMode = engineMode });
        var optInRegex = ByteRegex.Compile(Issue58Pattern, CreateOptions(engineMode));

        Assert.IsNull(defaultRegex.Find(noMatch));
        Assert.IsNull(optInRegex.Find(noMatch));
        Assert.AreEqual(0, defaultRegex.Count(noMatch));
        Assert.AreEqual(defaultRegex.Count(noMatch), optInRegex.Count(noMatch));

        ByteRegexMatch expected = new(1, positive.Length - 1);
        Assert.AreEqual(expected, defaultRegex.Find(positive));
        Assert.AreEqual(expected, optInRegex.Find(positive));
    }

    /// <summary>
    /// Verifies repetition counts every malformed or truncated byte as one replacement scalar.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void RepetitionConsumesEachInvalidByteAsOneScalar(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xC3, (byte)'(', 0xE2, 0x82];
        var regex = ByteRegex.Compile(@"(?:\u{FFFD})+", CreateOptions(engineMode));

        Assert.AreEqual(new ByteRegexMatch(0, 2), regex.Find(input));
        Assert.AreEqual(new ByteRegexMatch(3, 2), regex.Find(input, startAt: 2));
        Assert.AreEqual(2, regex.Count(input));
    }

    /// <summary>
    /// Verifies anchors, start offsets, and empty matches observe one scalar boundary per invalid byte.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void AnchorsStartAtAndEmptyMatchesUseInvalidByteScalarBoundaries(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xC3];
        ByteRegexOptions options = CreateOptions(engineMode);
        var anchored = ByteRegex.Compile(@"^(?:\u{FFFD}){2}$", options);
        var scalar = ByteRegex.Compile(@"\u{FFFD}", options);
        var empty = ByteRegex.Compile(string.Empty, options);

        Assert.AreEqual(new ByteRegexMatch(0, 2), anchored.Find(input));
        Assert.IsNull(anchored.Find(input, startAt: 1));
        Assert.AreEqual(new ByteRegexMatch(1, 1), scalar.Find(input, startAt: 1));
        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 0), new ByteRegexMatch(1, 0), new ByteRegexMatch(2, 0)],
            CollectMatches(empty, input));
    }

    /// <summary>
    /// Verifies captures and values point into the original malformed and valid UTF-8 bytes.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void CapturesAndValuesPreserveOriginalInvalidBytes(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xC3, 0xEF, 0xBF, 0xBD];
        var regex = ByteRegex.Compile(@"(\u{FFFD}+)(\u{FFFD})", CreateOptions(engineMode));

        ByteRegexCaptures? captures = regex.FindCaptures(input);

        Assert.IsNotNull(captures);
        Assert.AreEqual(new ByteRegexMatch(0, 5), captures.Match);
        Assert.AreEqual(new ByteRegexMatch(0, 5), captures.GetGroup(0));
        Assert.AreEqual(new ByteRegexMatch(0, 2), captures.GetGroup(1));
        Assert.AreEqual(new ByteRegexMatch(2, 3), captures.GetGroup(2));
        Assert.IsTrue(captures.Match.Value(input).SequenceEqual(input));
        Assert.IsTrue(captures.GetGroup(1)!.Value.Value(input).SequenceEqual(new byte[] { 0xFF, 0xC3 }));
        Assert.IsTrue(captures.GetGroup(2)!.Value.Value(input).SequenceEqual(new byte[] { 0xEF, 0xBF, 0xBD }));
    }

    /// <summary>
    /// Verifies aggregate and callback APIs report the original byte spans for invalid and valid replacement scalars.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void CountAndForEachMatchReportOriginalByteSpans(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, (byte)'x', 0xC3, 0xEF, 0xBF, 0xBD];
        var regex = ByteRegex.Compile(@"\u{FFFD}", CreateOptions(engineMode));

        List<ByteRegexMatch> matches = CollectMatches(regex, input);

        Assert.AreEqual(3, regex.Count(input));
        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(2, 1), new ByteRegexMatch(3, 3)],
            matches);
        Assert.IsTrue(matches[0].Value(input).SequenceEqual(new byte[] { 0xFF }));
        Assert.IsTrue(matches[1].Value(input).SequenceEqual(new byte[] { 0xC3 }));
        Assert.IsTrue(matches[2].Value(input).SequenceEqual(new byte[] { 0xEF, 0xBF, 0xBD }));
    }

    /// <summary>
    /// Verifies ordered regex sets and individual regexes agree on invalid UTF-8 matches and aggregate counts.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void RegexSetAgreesWithRegexForInvalidUtf8(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xC3, (byte)'x'];
        ByteRegexOptions options = CreateOptions(engineMode);
        var regex = ByteRegex.Compile(@"(?:x|\u{FFFD}+)", options);
        var set = ByteRegexSet.Compile(["(x)", @"(\u{FFFD}+)"], options);

        ByteRegexMatch? regexMatch = regex.Find(input);
        ByteRegexSetMatch? setMatch = set.Find(input);
        ByteRegexCaptures? setCaptures = set.FindCaptures(input);

        Assert.AreEqual(new ByteRegexMatch(0, 2), regexMatch);
        Assert.IsTrue(set.IsMatch(input));
        Assert.IsTrue(setMatch.HasValue);
        Assert.AreEqual(1, setMatch.Value.PatternId);
        Assert.AreEqual(regexMatch, setMatch.Value.Match);
        Assert.AreEqual(regex.Count(input), set.CountMatches(input));
        Assert.AreEqual(regex.Find(input, startAt: 1), set.Find(input, startAt: 1)!.Value.Match);
        Assert.AreEqual(regex.Count(input, startAt: 1), set.CountMatches(input, startAt: 1));
        Assert.IsNotNull(setCaptures);
        Assert.AreEqual(2, setCaptures.GroupCount);
        Assert.AreEqual(setMatch.Value.Match, setCaptures.Match);
        Assert.AreEqual(setMatch.Value.Match, setCaptures.GetGroup(0));
        Assert.AreEqual(setMatch.Value.Match, setCaptures.GetGroup(1));
        Assert.IsTrue(setCaptures.Match.Value(input).SequenceEqual(new byte[] { 0xFF, 0xC3 }));
    }

    /// <summary>
    /// Verifies valid two-, three-, and four-byte UTF-8 scalars remain indivisible.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void ValidMultibyteScalarsAreNeverSplit(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xC3, 0xA9, 0xE2, 0x82, 0xAC, 0xF0, 0x9F, 0x92, 0xA9];
        var regex = ByteRegex.Compile(".", CreateOptions(engineMode, dotMatchesNewline: true));

        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(0, 2), new ByteRegexMatch(2, 3), new ByteRegexMatch(5, 4)],
            CollectMatches(regex, input));
        Assert.AreEqual(new ByteRegexMatch(2, 3), regex.Find(input, startAt: 1));
        Assert.AreEqual(new ByteRegexMatch(5, 4), regex.Find(input, startAt: 3));
        Assert.IsNull(regex.Find(input, startAt: 6));
    }

    /// <summary>
    /// Verifies raw byte mode is byte-for-byte identical when replacement matching is enabled.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void RawByteModeIsUnaffectedByMatchInvalidUtf8(ByteRegexEngineMode engineMode)
    {
        byte[] pattern = [0xFF, (byte)'.'];
        byte[] input = [0x00, 0xFF, 0xFE];
        var disabled = ByteRegex.Compile(pattern, CreateRawOptions(engineMode, matchInvalidUtf8: false));
        var enabled = ByteRegex.Compile(pattern, CreateRawOptions(engineMode, matchInvalidUtf8: true));

        Assert.AreEqual(new ByteRegexMatch(1, 2), disabled.Find(input));
        Assert.AreEqual(disabled.Find(input), enabled.Find(input));
        Assert.AreEqual(disabled.Count(input), enabled.Count(input));
    }

    /// <summary>
    /// Verifies an inline Unicode disable takes precedence and can split valid UTF-8 into raw bytes.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void InlineRawByteModeTakesPrecedenceOverMatchInvalidUtf8(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xF0, 0x9F, 0x92, 0xA9];
        var regex = ByteRegex.Compile(@"(?-u:.)", CreateOptions(engineMode));

        Assert.AreEqual(new ByteRegexMatch(1, 1), regex.Find(input, startAt: 1));
        Assert.AreEqual(4, regex.Count(input));
    }

    /// <summary>
    /// Verifies direct invalid-byte matching agrees with a seeded valid-UTF-8 expansion and byte-offset oracle.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void SeededExpandedReplacementOracleAgreesWithDirectMatching(ByteRegexEngineMode engineMode)
    {
        string[] patterns =
        [
            ".",
            @"\u{FFFD}+",
            @"[\u{FFFD}a-z]+",
            @"\p{So}",
            "[^a-z]+",
            "^.*$",
        ];
        ByteRegexOptions directOptions = CreateOptions(engineMode);
        var oracleOptions = new ByteRegexOptions { EngineMode = engineMode };
        (ByteRegex Direct, ByteRegex Oracle)[] regexes = patterns
            .Select(pattern =>
                (ByteRegex.Compile(pattern, directOptions), ByteRegex.Compile(pattern, oracleOptions)))
            .ToArray();
        var directCaptures = ByteRegex.Compile(@"(\u{FFFD}+)([^a]?)", directOptions);
        var oracleCaptures = ByteRegex.Compile(@"(\u{FFFD}+)([^a]?)", oracleOptions);
        uint randomState = 0x56C0FFu;

        for (int caseIndex = 0; caseIndex < OracleInputCount; caseIndex++)
        {
            byte[] input = CreateRandomMalformedInput(ref randomState);
            (byte[] bytes, int[] boundaryMap) = ExpandInvalidUtf8(input);

            foreach ((ByteRegex direct, ByteRegex oracle) in regexes)
            {
                var expected = CollectMatches(oracle, bytes)
                    .Select(match => MapExpandedMatch(match, boundaryMap))
                    .ToList();

                Assert.AreSequenceEqual(expected, CollectMatches(direct, input));
                Assert.AreEqual(oracle.Count(bytes), direct.Count(input));
            }

            AssertMappedCapturesEqual(
                oracleCaptures.FindCaptures(bytes),
                directCaptures.FindCaptures(input),
                boundaryMap);
        }
    }

    /// <summary>
    /// Verifies warmed Count and callback iteration allocations remain independent of input size.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    [Timeout(30000, CooperativeCancellation = true)]
    public void AggregateAllocationsDoNotScaleWithInputLength(ByteRegexEngineMode engineMode)
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        byte[] small = CreateSparseInvalidInput(InvalidByteStride);
        byte[] large = CreateSparseInvalidInput(LargeAllocationInputLength);
        var regex = ByteRegex.Compile(@"\u{FFFD}", CreateOptions(engineMode));

        cancellationToken.ThrowIfCancellationRequested();
        _ = MeasureCountAllocations(regex, small);
        _ = MeasureCountAllocations(regex, large);
        _ = MeasureIterationAllocations(regex, small);
        _ = MeasureIterationAllocations(regex, large);

        cancellationToken.ThrowIfCancellationRequested();
        (long smallCount, long smallCountBytes) = MeasureCountAllocations(regex, small);
        (long largeCount, long largeCountBytes) = MeasureCountAllocations(regex, large);
        (long smallIterationCount, long smallIterationBytes) = MeasureIterationAllocations(regex, small);
        (long largeIterationCount, long largeIterationBytes) = MeasureIterationAllocations(regex, large);

        cancellationToken.ThrowIfCancellationRequested();
        Assert.AreEqual(1, smallCount);
        Assert.AreEqual(LargeAllocationInputLength / InvalidByteStride, largeCount);
        Assert.AreEqual(smallCount, smallIterationCount);
        Assert.AreEqual(largeCount, largeIterationCount);
        Assert.IsInRange(0, smallCountBytes + AllocationScalingSlack, largeCountBytes);
        Assert.IsInRange(0, smallIterationBytes + AllocationScalingSlack, largeIterationBytes);
    }

    /// <summary>
    /// Verifies compiled regex and set instances safely reuse their invalid-UTF-8 matching state across threads.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void CompiledRegexAndSetCanBeReusedConcurrently(ByteRegexEngineMode engineMode)
    {
        ByteRegexOptions options = CreateOptions(engineMode);
        var regex = ByteRegex.Compile(@"(\u{FFFD}+)", options);
        var set = ByteRegexSet.Compile([@"\u{FFFD}+", "x"], options);
        (byte[] Input, ByteRegexMatch Expected)[] cases =
        [
            ([0xFF, (byte)'x'], new ByteRegexMatch(0, 1)),
            ([0xC3, 0xE2, 0x82, (byte)'x'], new ByteRegexMatch(0, 3)),
            ([0xEF, 0xBF, 0xBD, (byte)'x'], new ByteRegexMatch(0, 3)),
        ];

        Parallel.For(0, ConcurrentIterations, _ =>
        {
            foreach ((byte[] input, ByteRegexMatch expected) in cases)
            {
                Assert.AreEqual(expected, regex.Find(input));
                Assert.AreEqual(expected, regex.FindCaptures(input)!.Match);

                ByteRegexSetMatch? setMatch = set.Find(input);
                Assert.IsTrue(setMatch.HasValue);
                Assert.AreEqual(0, setMatch.Value.PatternId);
                Assert.AreEqual(expected, setMatch.Value.Match);
            }
        });
    }

    private static ByteRegexOptions CreateOptions(
        ByteRegexEngineMode engineMode,
        bool dotMatchesNewline = false)
    {
        return new ByteRegexOptions
        {
            DotMatchesNewline = dotMatchesNewline,
            EngineMode = engineMode,
            MatchInvalidUtf8 = true,
        };
    }

    private static ByteRegexOptions CreateRawOptions(
        ByteRegexEngineMode engineMode,
        bool matchInvalidUtf8)
    {
        return new ByteRegexOptions
        {
            EngineMode = engineMode,
            MatchInvalidUtf8 = matchInvalidUtf8,
            UnicodeClasses = false,
            Utf8 = false,
        };
    }

    private static List<ByteRegexMatch> CollectMatches(ByteRegex regex, byte[] input)
    {
        var matches = new List<ByteRegexMatch>();

        int count = regex.ForEachMatch(input, ref matches, AddMatch);

        Assert.AreEqual(matches.Count, count);
        return matches;
    }

    private static byte[] CreateRandomMalformedInput(ref uint randomState)
    {
        byte[] input = new byte[OracleInputLength];
        input[0] = 0xFF;
        input[1] = 0xC3;
        input[2] = (byte)'(';
        FillPseudoRandomBytes(input.AsSpan(3, input.Length - 6), ref randomState);
        input[^3] = 0xEF;
        input[^2] = 0xBF;
        input[^1] = 0xBD;
        return input;
    }

    private static void FillPseudoRandomBytes(Span<byte> destination, ref uint state)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            destination[index] = (byte)state;
        }
    }

    private static (byte[] Bytes, int[] BoundaryMap) ExpandInvalidUtf8(byte[] input)
    {
        var bytes = new List<byte>(input.Length);
        var boundaryMap = new List<int>(input.Length + 1) { 0 };
        int inputOffset = 0;
        while (inputOffset < input.Length)
        {
            OperationStatus status = Rune.DecodeFromUtf8(
                input.AsSpan(inputOffset),
                out _,
                out int consumed);
            if (status == OperationStatus.Done)
            {
                for (int index = 0; index < consumed; index++)
                {
                    bytes.Add(input[inputOffset + index]);
                    boundaryMap.Add(inputOffset + index + 1);
                }

                inputOffset += consumed;
                continue;
            }

            bytes.Add(0xEF);
            boundaryMap.Add(inputOffset);
            bytes.Add(0xBF);
            boundaryMap.Add(inputOffset);
            bytes.Add(0xBD);
            inputOffset++;
            boundaryMap.Add(inputOffset);
        }

        return (bytes.ToArray(), boundaryMap.ToArray());
    }

    private static ByteRegexMatch MapExpandedMatch(ByteRegexMatch match, int[] boundaryMap)
    {
        int start = boundaryMap[match.Start];
        return new ByteRegexMatch(start, boundaryMap[match.End] - start);
    }

    private static void AssertMappedCapturesEqual(
        ByteRegexCaptures? oracle,
        ByteRegexCaptures? direct,
        int[] boundaryMap)
    {
        if (oracle is null)
        {
            Assert.IsNull(direct);
            return;
        }

        Assert.IsNotNull(direct);
        Assert.AreEqual(oracle.GroupCount, direct.GroupCount);
        Assert.AreEqual(MapExpandedMatch(oracle.Match, boundaryMap), direct.Match);
        for (int groupIndex = 0; groupIndex < oracle.GroupCount; groupIndex++)
        {
            ByteRegexMatch? oracleGroup = oracle.GetGroup(groupIndex);
            ByteRegexMatch? expected = oracleGroup.HasValue
                ? MapExpandedMatch(oracleGroup.Value, boundaryMap)
                : null;
            Assert.AreEqual(expected, direct.GetGroup(groupIndex));
        }
    }

    private static byte[] CreateSparseInvalidInput(int length)
    {
        byte[] input = new byte[length];
        input.AsSpan().Fill((byte)'a');
        for (int index = 0; index < input.Length; index += InvalidByteStride)
        {
            input[index] = 0xFF;
        }

        return input;
    }

    private static byte[] CreateIssue58NoMatchInput(int length)
    {
        byte[] input = GC.AllocateUninitializedArray<byte>(length);
        input.AsSpan().Fill(0xFF);
        ReadOnlySpan<byte> candidate = "api_key = invalid-candidate "u8;
        for (int offset = 0; offset <= input.Length - candidate.Length; offset += InvalidByteStride)
        {
            candidate.CopyTo(input.AsSpan(offset));
        }

        return input;
    }

    private static byte[] CreateIssue58PositiveInput()
    {
        string token = $"ey{new string('A', 17)}.ey{new string('B', 17)}.{new string('C', 10)}";
        byte[] tokenBytes = Encoding.ASCII.GetBytes(token);
        byte[] input = GC.AllocateUninitializedArray<byte>(tokenBytes.Length + 1);
        input[0] = 0xFF;
        tokenBytes.CopyTo(input, 1);
        return input;
    }

    private static (long Count, long AllocatedBytes) MeasureCountAllocations(
        ByteRegex regex,
        byte[] input)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long count = regex.Count(input);
        return (count, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static (long Count, long AllocatedBytes) MeasureIterationAllocations(
        ByteRegex regex,
        byte[] input)
    {
        long state = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = regex.ForEachMatch(input, ref state, CountMatch);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(state, count);
        return (count, allocatedBytes);
    }

    private static bool AddMatch(
        ReadOnlySpan<byte> input,
        ByteRegexMatch match,
        ref List<ByteRegexMatch> state)
    {
        state.Add(match);
        return true;
    }

    private static bool CountMatch(
        ReadOnlySpan<byte> input,
        ByteRegexMatch match,
        ref long state)
    {
        state++;
        return true;
    }
}
