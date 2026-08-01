using System.Buffers;
using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies malformed UTF-8 can opt into replacement-scalar matching without losing byte offsets.
/// </summary>
public sealed class ByteRegexInvalidUtf8Tests
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
    [Fact]
    public void MatchInvalidUtf8DefaultsToFalseAndPreservesDefaultMatching()
    {
        var options = new ByteRegexOptions();
        byte[] input = [0xFF, 0xEF, 0xBF, 0xBD];

        var regex = ByteRegex.Compile(@"\u{FFFD}", options);

        Assert.False(options.MatchInvalidUtf8);
        Assert.True(regex.IsMatch(input));
        Assert.Equal(new ByteRegexMatch(1, 3), regex.Find(input));
        Assert.Equal(1, regex.Count(input));
    }

    /// <summary>
    /// Verifies representative malformed forms expose every original byte as an individual replacement scalar.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
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
            Assert.Equal(new ByteRegexMatch(0, input.Length), regex.Find(input));
            Assert.Equal(1, regex.Count(input));
        }
    }

    /// <summary>
    /// Verifies dot consumes each malformed byte but continues to exclude the configured newline unless dot-all is enabled.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void DotMatchesInvalidBytesButStillHonorsNewlineConfiguration(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, (byte)'\n', 0xE2, 0x82];
        var dot = ByteRegex.Compile(".", CreateOptions(engineMode));
        var dotAll = ByteRegex.Compile(
            ".",
            CreateOptions(engineMode, dotMatchesNewline: true));

        Assert.Equal(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(2, 1), new ByteRegexMatch(3, 1)],
            CollectMatches(dot, input));
        Assert.Equal(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(1, 1), new ByteRegexMatch(2, 1), new ByteRegexMatch(3, 1)],
            CollectMatches(dotAll, input));
    }

    /// <summary>
    /// Verifies scalar literals and classes match malformed bytes as U+FFFD while retaining the width of valid U+FFFD.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void ScalarSyntaxMatchesInvalidBytesAndEncodedReplacementCharacter(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xEF, 0xBF, 0xBD];
        string[] patterns = [@"\u{FFFD}", @"[\u{FFFD}]", @"\p{So}", "[^a]"];

        foreach (string pattern in patterns)
        {
            var regex = ByteRegex.Compile(pattern, CreateOptions(engineMode));

            Assert.Equal(new ByteRegexMatch(0, 1), regex.Find(input));
            Assert.Equal(new ByteRegexMatch(1, 3), regex.Find(input, startAt: 1));
            Assert.Equal(2, regex.Count(input));
        }
    }

    /// <summary>
    /// Verifies a scalar escape in a class does not broaden its ASCII membership.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void ScalarClassKeepsUnlistedAsciiOutsideMalformedRuns(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, (byte)'F', 0xEF, 0xBF, 0xBD];
        var regex = ByteRegex.Compile(@"[\u{FFFD}a-z]+", CreateOptions(engineMode));

        Assert.Equal(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(2, 3)],
            CollectMatches(regex, input));
    }

    /// <summary>
    /// Verifies invalid bytes have the Unicode word-boundary behavior of the non-word U+FFFD scalar.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void UnicodeWordBoundariesTreatInvalidBytesAsReplacementScalars(ByteRegexEngineMode engineMode)
    {
        byte[] input = [(byte)'a', 0xFF, (byte)'b'];
        var boundary = ByteRegex.Compile(@"\b", CreateOptions(engineMode));
        var word = ByteRegex.Compile(@"\w+", CreateOptions(engineMode));
        var invalidPair = ByteRegex.Compile(@"\u{FFFD}\B\u{FFFD}", CreateOptions(engineMode));

        Assert.Equal(
            [
                new ByteRegexMatch(0, 0),
                new ByteRegexMatch(1, 0),
                new ByteRegexMatch(2, 0),
                new ByteRegexMatch(3, 0),
            ],
            CollectMatches(boundary, input));
        Assert.Equal(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(2, 1)],
            CollectMatches(word, input));
        Assert.Equal(
            new ByteRegexMatch(0, 2),
            invalidPair.Find(new byte[] { 0xFF, 0xC3 }));
    }

    /// <summary>
    /// Verifies every Unicode word predicate evaluates malformed context without scalar consumption.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void UnicodeWordPredicatesUseInPlaceMalformedContext(ByteRegexEngineMode engineMode)
    {
        byte[] mixed = [(byte)'a', 0xFF, (byte)'b'];
        ByteRegexOptions options = CreateOptions(engineMode);

        Assert.Equal(
            [new ByteRegexMatch(0, 0), new ByteRegexMatch(2, 0)],
            CollectMatches(ByteRegex.Compile(@"\b{start}", options), mixed));
        Assert.Equal(
            [new ByteRegexMatch(1, 0), new ByteRegexMatch(3, 0)],
            CollectMatches(ByteRegex.Compile(@"\b{end}", options), mixed));
        Assert.Equal(
            [new ByteRegexMatch(0, 0), new ByteRegexMatch(2, 0)],
            CollectMatches(ByteRegex.Compile(@"\b{start-half}", options), mixed));
        Assert.Equal(
            [new ByteRegexMatch(1, 0), new ByteRegexMatch(3, 0)],
            CollectMatches(ByteRegex.Compile(@"\b{end-half}", options), mixed));
        Assert.Equal(
            [new ByteRegexMatch(0, 0), new ByteRegexMatch(1, 0), new ByteRegexMatch(2, 0)],
            CollectMatches(ByteRegex.Compile(@"\B", options), [0xFF, 0xC3]));
    }

    /// <summary>
    /// Verifies boundary alternation keeps observable capture participation on malformed input.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void BoundaryAlternationPreservesCapturesOnMalformedInput(ByteRegexEngineMode engineMode)
    {
        var regex = ByteRegex.Compile(@"(?u:(\b)|(\B))(?-u:.)", CreateOptions(engineMode));

        ByteRegexCaptures? boundary = regex.FindCaptures("a"u8);
        ByteRegexCaptures? nonBoundary = regex.FindCaptures([0xFF]);

        Assert.NotNull(boundary);
        Assert.Equal(new ByteRegexMatch(0, 1), boundary.Match);
        Assert.Equal(new ByteRegexMatch(0, 0), boundary.GetGroup(1));
        Assert.Null(boundary.GetGroup(2));
        Assert.NotNull(nonBoundary);
        Assert.Equal(new ByteRegexMatch(0, 1), nonBoundary.Match);
        Assert.Null(nonBoundary.GetGroup(1));
        Assert.Equal(new ByteRegexMatch(0, 0), nonBoundary.GetGroup(2));
    }

    /// <summary>
    /// Verifies the reported boundary-prefixed pattern avoids false matches without losing valid matches.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void Issue58BoundaryPrefixMatchesThroughSyntheticMalformedInput(ByteRegexEngineMode engineMode)
    {
        byte[] noMatch = CreateIssue58NoMatchInput(Issue58RegressionInputLength);
        byte[] positive = CreateIssue58PositiveInput();
        var defaultRegex = ByteRegex.Compile(
            Issue58Pattern,
            new ByteRegexOptions { EngineMode = engineMode });
        var optInRegex = ByteRegex.Compile(Issue58Pattern, CreateOptions(engineMode));

        Assert.Null(defaultRegex.Find(noMatch));
        Assert.Null(optInRegex.Find(noMatch));
        Assert.Equal(0, defaultRegex.Count(noMatch));
        Assert.Equal(defaultRegex.Count(noMatch), optInRegex.Count(noMatch));

        ByteRegexMatch expected = new(1, positive.Length - 1);
        Assert.Equal(expected, defaultRegex.Find(positive));
        Assert.Equal(expected, optInRegex.Find(positive));
    }

    /// <summary>
    /// Verifies repetition counts every malformed or truncated byte as one replacement scalar.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void RepetitionConsumesEachInvalidByteAsOneScalar(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xC3, (byte)'(', 0xE2, 0x82];
        var regex = ByteRegex.Compile(@"(?:\u{FFFD})+", CreateOptions(engineMode));

        Assert.Equal(new ByteRegexMatch(0, 2), regex.Find(input));
        Assert.Equal(new ByteRegexMatch(3, 2), regex.Find(input, startAt: 2));
        Assert.Equal(2, regex.Count(input));
    }

    /// <summary>
    /// Verifies anchors, start offsets, and empty matches observe one scalar boundary per invalid byte.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void AnchorsStartAtAndEmptyMatchesUseInvalidByteScalarBoundaries(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xC3];
        ByteRegexOptions options = CreateOptions(engineMode);
        var anchored = ByteRegex.Compile(@"^(?:\u{FFFD}){2}$", options);
        var scalar = ByteRegex.Compile(@"\u{FFFD}", options);
        var empty = ByteRegex.Compile(string.Empty, options);

        Assert.Equal(new ByteRegexMatch(0, 2), anchored.Find(input));
        Assert.Null(anchored.Find(input, startAt: 1));
        Assert.Equal(new ByteRegexMatch(1, 1), scalar.Find(input, startAt: 1));
        Assert.Equal(
            [new ByteRegexMatch(0, 0), new ByteRegexMatch(1, 0), new ByteRegexMatch(2, 0)],
            CollectMatches(empty, input));
    }

    /// <summary>
    /// Verifies captures and values point into the original malformed and valid UTF-8 bytes.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void CapturesAndValuesPreserveOriginalInvalidBytes(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xC3, 0xEF, 0xBF, 0xBD];
        var regex = ByteRegex.Compile(@"(\u{FFFD}+)(\u{FFFD})", CreateOptions(engineMode));

        ByteRegexCaptures? captures = regex.FindCaptures(input);

        Assert.NotNull(captures);
        Assert.Equal(new ByteRegexMatch(0, 5), captures.Match);
        Assert.Equal(new ByteRegexMatch(0, 5), captures.GetGroup(0));
        Assert.Equal(new ByteRegexMatch(0, 2), captures.GetGroup(1));
        Assert.Equal(new ByteRegexMatch(2, 3), captures.GetGroup(2));
        Assert.True(captures.Match.Value(input).SequenceEqual(input));
        Assert.True(captures.GetGroup(1)!.Value.Value(input).SequenceEqual(new byte[] { 0xFF, 0xC3 }));
        Assert.True(captures.GetGroup(2)!.Value.Value(input).SequenceEqual(new byte[] { 0xEF, 0xBF, 0xBD }));
    }

    /// <summary>
    /// Verifies aggregate and callback APIs report the original byte spans for invalid and valid replacement scalars.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void CountAndForEachMatchReportOriginalByteSpans(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, (byte)'x', 0xC3, 0xEF, 0xBF, 0xBD];
        var regex = ByteRegex.Compile(@"\u{FFFD}", CreateOptions(engineMode));

        List<ByteRegexMatch> matches = CollectMatches(regex, input);

        Assert.Equal(3, regex.Count(input));
        Assert.Equal(
            [new ByteRegexMatch(0, 1), new ByteRegexMatch(2, 1), new ByteRegexMatch(3, 3)],
            matches);
        Assert.True(matches[0].Value(input).SequenceEqual(new byte[] { 0xFF }));
        Assert.True(matches[1].Value(input).SequenceEqual(new byte[] { 0xC3 }));
        Assert.True(matches[2].Value(input).SequenceEqual(new byte[] { 0xEF, 0xBF, 0xBD }));
    }

    /// <summary>
    /// Verifies ordered regex sets and individual regexes agree on invalid UTF-8 matches and aggregate counts.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void RegexSetAgreesWithRegexForInvalidUtf8(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xFF, 0xC3, (byte)'x'];
        ByteRegexOptions options = CreateOptions(engineMode);
        var regex = ByteRegex.Compile(@"(?:x|\u{FFFD}+)", options);
        var set = ByteRegexSet.Compile(["(x)", @"(\u{FFFD}+)"], options);

        ByteRegexMatch? regexMatch = regex.Find(input);
        ByteRegexSetMatch? setMatch = set.Find(input);
        ByteRegexCaptures? setCaptures = set.FindCaptures(input);

        Assert.Equal(new ByteRegexMatch(0, 2), regexMatch);
        Assert.True(set.IsMatch(input));
        Assert.True(setMatch.HasValue);
        Assert.Equal(1, setMatch.Value.PatternId);
        Assert.Equal(regexMatch, setMatch.Value.Match);
        Assert.Equal(regex.Count(input), set.CountMatches(input));
        Assert.Equal(regex.Find(input, startAt: 1), set.Find(input, startAt: 1)!.Value.Match);
        Assert.Equal(regex.Count(input, startAt: 1), set.CountMatches(input, startAt: 1));
        Assert.NotNull(setCaptures);
        Assert.Equal(2, setCaptures.GroupCount);
        Assert.Equal(setMatch.Value.Match, setCaptures.Match);
        Assert.Equal(setMatch.Value.Match, setCaptures.GetGroup(0));
        Assert.Equal(setMatch.Value.Match, setCaptures.GetGroup(1));
        Assert.True(setCaptures.Match.Value(input).SequenceEqual(new byte[] { 0xFF, 0xC3 }));
    }

    /// <summary>
    /// Verifies valid two-, three-, and four-byte UTF-8 scalars remain indivisible.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void ValidMultibyteScalarsAreNeverSplit(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xC3, 0xA9, 0xE2, 0x82, 0xAC, 0xF0, 0x9F, 0x92, 0xA9];
        var regex = ByteRegex.Compile(".", CreateOptions(engineMode, dotMatchesNewline: true));

        Assert.Equal(
            [new ByteRegexMatch(0, 2), new ByteRegexMatch(2, 3), new ByteRegexMatch(5, 4)],
            CollectMatches(regex, input));
        Assert.Equal(new ByteRegexMatch(2, 3), regex.Find(input, startAt: 1));
        Assert.Equal(new ByteRegexMatch(5, 4), regex.Find(input, startAt: 3));
        Assert.Null(regex.Find(input, startAt: 6));
    }

    /// <summary>
    /// Verifies raw byte mode is byte-for-byte identical when replacement matching is enabled.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void RawByteModeIsUnaffectedByMatchInvalidUtf8(ByteRegexEngineMode engineMode)
    {
        byte[] pattern = [0xFF, (byte)'.'];
        byte[] input = [0x00, 0xFF, 0xFE];
        var disabled = ByteRegex.Compile(pattern, CreateRawOptions(engineMode, matchInvalidUtf8: false));
        var enabled = ByteRegex.Compile(pattern, CreateRawOptions(engineMode, matchInvalidUtf8: true));

        Assert.Equal(new ByteRegexMatch(1, 2), disabled.Find(input));
        Assert.Equal(disabled.Find(input), enabled.Find(input));
        Assert.Equal(disabled.Count(input), enabled.Count(input));
    }

    /// <summary>
    /// Verifies an inline Unicode disable takes precedence and can split valid UTF-8 into raw bytes.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void InlineRawByteModeTakesPrecedenceOverMatchInvalidUtf8(ByteRegexEngineMode engineMode)
    {
        byte[] input = [0xF0, 0x9F, 0x92, 0xA9];
        var regex = ByteRegex.Compile(@"(?-u:.)", CreateOptions(engineMode));

        Assert.Equal(new ByteRegexMatch(1, 1), regex.Find(input, startAt: 1));
        Assert.Equal(4, regex.Count(input));
    }

    /// <summary>
    /// Verifies direct invalid-byte matching agrees with a seeded valid-UTF-8 expansion and byte-offset oracle.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
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

                Assert.Equal(expected, CollectMatches(direct, input));
                Assert.Equal(oracle.Count(bytes), direct.Count(input));
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
    [Theory(Timeout = 30000)]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void AggregateAllocationsDoNotScaleWithInputLength(ByteRegexEngineMode engineMode)
    {
        byte[] small = CreateSparseInvalidInput(InvalidByteStride);
        byte[] large = CreateSparseInvalidInput(LargeAllocationInputLength);
        var regex = ByteRegex.Compile(@"\u{FFFD}", CreateOptions(engineMode));

        _ = MeasureCountAllocations(regex, small);
        _ = MeasureCountAllocations(regex, large);
        _ = MeasureIterationAllocations(regex, small);
        _ = MeasureIterationAllocations(regex, large);

        (long smallCount, long smallCountBytes) = MeasureCountAllocations(regex, small);
        (long largeCount, long largeCountBytes) = MeasureCountAllocations(regex, large);
        (long smallIterationCount, long smallIterationBytes) = MeasureIterationAllocations(regex, small);
        (long largeIterationCount, long largeIterationBytes) = MeasureIterationAllocations(regex, large);

        Assert.Equal(1, smallCount);
        Assert.Equal(LargeAllocationInputLength / InvalidByteStride, largeCount);
        Assert.Equal(smallCount, smallIterationCount);
        Assert.Equal(largeCount, largeIterationCount);
        Assert.InRange(largeCountBytes, 0, smallCountBytes + AllocationScalingSlack);
        Assert.InRange(largeIterationBytes, 0, smallIterationBytes + AllocationScalingSlack);
    }

    /// <summary>
    /// Verifies compiled regex and set instances safely reuse their invalid-UTF-8 matching state across threads.
    /// </summary>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
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
                Assert.Equal(expected, regex.Find(input));
                Assert.Equal(expected, regex.FindCaptures(input)!.Match);

                ByteRegexSetMatch? setMatch = set.Find(input);
                Assert.True(setMatch.HasValue);
                Assert.Equal(0, setMatch.Value.PatternId);
                Assert.Equal(expected, setMatch.Value.Match);
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

        Assert.Equal(matches.Count, count);
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
            Assert.Null(direct);
            return;
        }

        Assert.NotNull(direct);
        Assert.Equal(oracle.GroupCount, direct.GroupCount);
        Assert.Equal(MapExpandedMatch(oracle.Match, boundaryMap), direct.Match);
        for (int groupIndex = 0; groupIndex < oracle.GroupCount; groupIndex++)
        {
            ByteRegexMatch? oracleGroup = oracle.GetGroup(groupIndex);
            ByteRegexMatch? expected = oracleGroup.HasValue
                ? MapExpandedMatch(oracleGroup.Value, boundaryMap)
                : null;
            Assert.Equal(expected, direct.GetGroup(groupIndex));
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
        Assert.Equal(state, count);
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
