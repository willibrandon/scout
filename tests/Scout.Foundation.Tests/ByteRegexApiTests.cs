using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the public byte regex facade intended for package consumers.
/// </summary>
public sealed class ByteRegexApiTests
{
    private const int BoundedAssignmentSearchTimeoutMilliseconds = 5000;
    private const int LargeBoundedUnicodeClassCandidateCount = 5000;
    private const int LargeBoundedUnicodeClassSearchTimeoutMilliseconds = 5000;
    private const int RepeatedBoundedAssignmentSearchTimeoutMilliseconds = 5000;
    private const int RepeatedBoundedAssignmentCandidateCount = 800;
    private const int RepeatedBoundedAssignmentStressCandidateCount = 4000;
    private const long RepeatedBoundedAssignmentAllocationLimit = 64 * 1024;
    private const int ConcurrentSearchIterations = 64;
    private const int ConcurrentHaystackCount = 8;
    private const string BoundedAssignmentPattern = "(?i)[\\w.-]{0,50}?(?:adafruit)(?:[ \\t\\w.-]{0,20})[\\s'\"]{0,3}(?:=|>|:{1,3}=|\\|\\||:|=>|\\?=|,)[\\x60'\"\\s=]{0,5}([a-z0-9_-]{32})(?:[\\x60'\"\\s;]|\\\\[nr]|$)";
    private const string LargeBoundedUnicodeClassPattern = "x[\\w-]{50,1000}";
    private const string RepeatedBoundedAssignmentPattern = "(?i)[\\w.-]{0,50}?(?:bitbucket)(?:[ \\t\\w.-]{0,20})[\\s'\"]{0,3}(?:=|>|:{1,3}=|\\|\\||:|=>|\\?=|,)[\\x60'\"\\s=]{0,5}([a-z0-9]{32})(?:[\\x60'\"\\s;]|\\\\[nr]|$)";

    /// <summary>
    /// Verifies byte regex matching exposes byte offsets and spans.
    /// </summary>
    [Fact]
    public void FindsFirstMatch()
    {
        var regex = ByteRegex.Compile(@"(?i)[[:alpha:]]+\d+");

        ByteRegexMatch? match = regex.Find("11ABC123 yy"u8);

        Assert.True(match.HasValue);
        Assert.Equal(new ByteRegexMatch(2, 6), match.Value);
        Assert.True(match.Value.Value("11ABC123 yy"u8).SequenceEqual("ABC123"u8));
        Assert.True(regex.IsMatch("ABC123"u8));
        Assert.False(regex.IsMatch("ABC"u8));
    }

    /// <summary>
    /// Verifies capture groups are exposed without leaking automata types.
    /// </summary>
    [Fact]
    public void FindsCaptures()
    {
        var regex = ByteRegex.Compile(@"([[:alpha:]]+)(\d+)");

        ByteRegexCaptures? captures = regex.FindCaptures("11ABC123 yy"u8);

        Assert.NotNull(captures);
        Assert.Equal(3, captures.GroupCount);
        Assert.Equal(new ByteRegexMatch(2, 6), captures.Match);
        Assert.Equal(new ByteRegexMatch(2, 6), captures.GetGroup(0));
        Assert.Equal(new ByteRegexMatch(2, 3), captures.GetGroup(1));
        Assert.Equal(new ByteRegexMatch(5, 3), captures.GetGroup(2));
        Assert.Equal(3, captures.ParticipatingCount());
    }

    /// <summary>
    /// Verifies bounded assignment patterns with Unicode classes do not expand into pathological VM searches.
    /// </summary>
    [Fact(Timeout = BoundedAssignmentSearchTimeoutMilliseconds)]
    public void FindsBoundedAssignmentCapturesWithoutStalling()
    {
        var regex = ByteRegex.Compile(BoundedAssignmentPattern);
        byte[] positive = Encoding.UTF8.GetBytes("adafruit_api_key = abc123def456ghi789jkl012mno345pq\n");
        byte[] negative = Encoding.UTF8.GetBytes("regex = '''(?i)[\\\\w.-]{0,50}?(?:adafruit)(?:[ \\\\t\\\\w.-]{0,20})''' keywords = [\"adafruit\"]");

        ByteRegexMatch? match = regex.Find(positive);
        ByteRegexCaptures? captures = regex.FindCaptures(positive);

        Assert.True(match.HasValue);
        Assert.NotNull(captures);
        Assert.Equal(match.Value, captures.Match);
        ByteRegexMatch? secret = captures.GetGroup(1);
        Assert.True(secret.HasValue);
        Assert.True(secret.Value.Value(positive).SequenceEqual("abc123def456ghi789jkl012mno345pq"u8));
        Assert.Null(regex.Find(negative));
        Assert.False(regex.IsMatch(negative));
        Assert.Equal(0, regex.Count(negative));
    }

    /// <summary>
    /// Verifies a large bounded Unicode class compiles and rejects the issue 32 candidate set without stalling.
    /// </summary>
    [Fact(Timeout = LargeBoundedUnicodeClassSearchTimeoutMilliseconds)]
    public void RejectsLargeBoundedUnicodeClassCandidatesWithoutStalling()
    {
        var regex = ByteRegex.Compile(
            LargeBoundedUnicodeClassPattern,
            new ByteRegexOptions { EngineMode = ByteRegexEngineMode.AutomataOnly });
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat(
                LargeBoundedUnicodeClassPattern + "\n",
                LargeBoundedUnicodeClassCandidateCount)));

        Assert.Null(regex.Find(input));
    }

    /// <summary>
    /// Verifies a large set of repeated nonmatching bounded-assignment candidates is rejected without stalling.
    /// </summary>
    [Fact(Timeout = RepeatedBoundedAssignmentSearchTimeoutMilliseconds)]
    public void RejectsManyRepeatedBoundedAssignmentCandidatesWithoutRescanning()
    {
        var regex = ByteRegex.Compile(RepeatedBoundedAssignmentPattern);
        byte[] input = CreateRepeatedBoundedAssignmentInput(RepeatedBoundedAssignmentStressCandidateCount);

        Assert.Null(regex.Find(input));
    }

    /// <summary>
    /// Verifies every public engine mode rejects repeated candidates consistently across search operations.
    /// </summary>
    [Theory(Timeout = RepeatedBoundedAssignmentSearchTimeoutMilliseconds)]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void RejectsRepeatedBoundedAssignmentCandidatesAcrossEngineModes(ByteRegexEngineMode engineMode)
    {
        var regex = ByteRegex.Compile(
            RepeatedBoundedAssignmentPattern,
            new ByteRegexOptions { EngineMode = engineMode });
        byte[] terminalMatch = Encoding.UTF8.GetBytes("bitbucket = abc123def456ghi789jkl012mno345pq");
        byte[] input = CreateRepeatedBoundedAssignmentInput(RepeatedBoundedAssignmentCandidateCount);

        ByteRegexCaptures? captures = regex.FindCaptures(terminalMatch);
        Assert.NotNull(captures);
        Assert.Equal(new ByteRegexMatch(0, terminalMatch.Length), captures.Match);
        ByteRegexMatch? secret = captures.GetGroup(1);
        Assert.Equal(new ByteRegexMatch(12, 32), secret);
        Assert.True(secret!.Value.Value(terminalMatch).SequenceEqual("abc123def456ghi789jkl012mno345pq"u8));
        Assert.Null(regex.Find(input));
        Assert.False(regex.IsMatch(input));
        Assert.Equal(0, regex.Count(input));
        Assert.Null(regex.FindCaptures(input));
    }

    /// <summary>
    /// Verifies warmed repeated-candidate match and capture searches reuse scratch instead of allocating per candidate.
    /// </summary>
    [Theory(Timeout = RepeatedBoundedAssignmentSearchTimeoutMilliseconds)]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void ReusesScratchForRepeatedBoundedAssignmentSearches(ByteRegexEngineMode engineMode)
    {
        var regex = ByteRegex.Compile(
            RepeatedBoundedAssignmentPattern,
            new ByteRegexOptions { EngineMode = engineMode });
        byte[] input = CreateRepeatedBoundedAssignmentInput(RepeatedBoundedAssignmentCandidateCount);
        Assert.Null(regex.Find(input));
        Assert.Null(regex.FindCaptures(input));

        long findBefore = GC.GetAllocatedBytesForCurrentThread();
        ByteRegexMatch? match = regex.Find(input);
        long findAllocated = GC.GetAllocatedBytesForCurrentThread() - findBefore;

        long capturesBefore = GC.GetAllocatedBytesForCurrentThread();
        ByteRegexCaptures? captures = regex.FindCaptures(input);
        long capturesAllocated = GC.GetAllocatedBytesForCurrentThread() - capturesBefore;

        Assert.Null(match);
        Assert.Null(captures);
        Assert.InRange(findAllocated, 0, RepeatedBoundedAssignmentAllocationLimit);
        Assert.InRange(capturesAllocated, 0, RepeatedBoundedAssignmentAllocationLimit);
    }

    /// <summary>
    /// Verifies byte mode can search arbitrary non-UTF-8 input.
    /// </summary>
    [Fact]
    public void MatchesArbitraryBytesWhenUtf8BoundaryChecksAreDisabled()
    {
        byte[] pattern = [0xff, (byte)'.'];
        var regex = ByteRegex.Compile(
            pattern,
            new ByteRegexOptions
            {
                Utf8 = false,
                UnicodeClasses = false,
                EngineMode = ByteRegexEngineMode.General,
            });
        byte[] input = [0x00, 0xff, 0xfe, 0x41];

        Assert.Equal(new ByteRegexMatch(1, 2), regex.Find(input));
    }

    /// <summary>
    /// Verifies match iteration uses caller-owned state.
    /// </summary>
    [Fact]
    public void IteratesMatchesWithState()
    {
        var regex = ByteRegex.Compile(@"\w+");
        var matches = new List<ByteRegexMatch>();

        int count = regex.ForEachMatch(
            "one two 3"u8,
            ref matches,
            AddMatch);

        Assert.Equal(3, count);
        Assert.Equal([new ByteRegexMatch(0, 3), new ByteRegexMatch(4, 3), new ByteRegexMatch(8, 1)], matches);
    }

    /// <summary>
    /// Verifies syntax errors are surfaced as byte regex parse exceptions.
    /// </summary>
    [Fact]
    public void ConvertsSyntaxErrorsToParseException()
    {
        ByteRegexParseException exception = Assert.Throws<ByteRegexParseException>(() => ByteRegex.Compile("["));

        Assert.Contains("byte offset", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.Offset);
    }

    /// <summary>
    /// Verifies ordered set matching is available through the public facade.
    /// </summary>
    [Fact]
    public void FindsSetMatch()
    {
        var set = ByteRegexSet.Compile(["foo[0-9]+", "bar[a-z]+"]);

        ByteRegexSetMatch? match = set.Find("xx barzz foo42"u8);

        Assert.True(match.HasValue);
        Assert.Equal(new ByteRegexSetMatch(1, new ByteRegexMatch(3, 5)), match.Value);
        Assert.True(set.IsMatch("foo42"u8));
        Assert.Equal(2, set.CountMatches("foo1 barz"u8));
    }

    /// <summary>
    /// Verifies a compiled regex can be shared across concurrent lazy-DFA searches.
    /// </summary>
    [Fact]
    public void SharedRegexFindsFromMultipleThreads()
    {
        var regex = ByteRegex.Compile(
            @"func \w+",
            new ByteRegexOptions
            {
                MultiLine = true,
                UnicodeClasses = true,
                EngineMode = ByteRegexEngineMode.General,
            });
        byte[][] haystacks = CreateConcurrentRegexHaystacks();

        Parallel.For(0, ConcurrentSearchIterations, _ =>
        {
            foreach (byte[] haystack in haystacks)
            {
                ByteRegexMatch? match = regex.Find(haystack);
                AssertFunctionMatch(match, haystack);
                Assert.True(regex.IsMatch(haystack));

                var matches = new List<ByteRegexMatch>();
                int count = regex.ForEachMatch(haystack, ref matches, AddMatch);

                Assert.Equal(1, count);
                Assert.Single(matches);
                Assert.Equal(match!.Value, matches[0]);
            }
        });
    }

    /// <summary>
    /// Verifies a compiled regex can be shared across concurrent PikeVM searches.
    /// </summary>
    [Fact]
    public void SharedRegexPikeVmFallbackFindsFromMultipleThreads()
    {
        var regex = ByteRegex.Compile(
            @"func \w+",
            new ByteRegexOptions
            {
                MultiLine = true,
                UnicodeClasses = true,
                DfaSizeLimit = 1,
                EngineMode = ByteRegexEngineMode.General,
            });
        byte[][] haystacks = CreateConcurrentRegexHaystacks();

        Parallel.For(0, ConcurrentSearchIterations, _ =>
        {
            foreach (byte[] haystack in haystacks)
            {
                AssertFunctionMatch(regex.Find(haystack), haystack);
                Assert.True(regex.IsMatch(haystack));
                Assert.Equal(1, regex.Count(haystack));
            }
        });
    }

    /// <summary>
    /// Verifies the repeated-candidate PikeVM search state can be leased safely by concurrent callers.
    /// </summary>
    [Fact(Timeout = RepeatedBoundedAssignmentSearchTimeoutMilliseconds)]
    public void SharedBoundedAssignmentRegexRejectsCandidatesFromMultipleThreads()
    {
        var regex = ByteRegex.Compile(
            RepeatedBoundedAssignmentPattern,
            new ByteRegexOptions { EngineMode = ByteRegexEngineMode.AutomataOnly });
        byte[] input = CreateRepeatedBoundedAssignmentInput(16);

        Parallel.For(0, ConcurrentSearchIterations, _ =>
        {
            Assert.Null(regex.Find(input));
            Assert.False(regex.IsMatch(input));
        });
    }

    /// <summary>
    /// Verifies generic capture matching can be shared across concurrent searches.
    /// </summary>
    [Fact]
    public void SharedRegexCapturesFromMultipleThreads()
    {
        var regex = ByteRegex.Compile(
            @"func ([a-z_]+)",
            new ByteRegexOptions
            {
                MultiLine = true,
                UnicodeClasses = false,
                EngineMode = ByteRegexEngineMode.AutomataOnly,
            });
        byte[][] haystacks = CreateConcurrentRegexHaystacks();

        Parallel.For(0, ConcurrentSearchIterations, _ =>
        {
            foreach (byte[] haystack in haystacks)
            {
                ByteRegexCaptures? captures = regex.FindCaptures(haystack);

                Assert.NotNull(captures);
                AssertFunctionMatch(captures.Match, haystack);
                ByteRegexMatch? group = captures.GetGroup(1);
                Assert.True(group.HasValue);
                Assert.True(group.Value.Value(haystack).StartsWith("handler_"u8));
            }
        });
    }

    /// <summary>
    /// Verifies a compiled regex set can be shared across concurrent searches.
    /// </summary>
    [Fact]
    public void SharedRegexSetFindsFromMultipleThreads()
    {
        var set = ByteRegexSet.Compile(
            ["func \\w+", "return \\w+"],
            new ByteRegexOptions
            {
                MultiLine = true,
                UnicodeClasses = true,
                DfaSizeLimit = 1,
                EngineMode = ByteRegexEngineMode.General,
            });
        byte[][] haystacks = CreateConcurrentRegexHaystacks();

        Parallel.For(0, ConcurrentSearchIterations, _ =>
        {
            foreach (byte[] haystack in haystacks)
            {
                ByteRegexSetMatch? match = set.Find(haystack);

                Assert.True(match.HasValue);
                Assert.Equal(0, match.Value.PatternId);
                AssertFunctionMatch(match.Value.Match, haystack);
            }
        });
    }

    private static bool AddMatch(ReadOnlySpan<byte> input, ByteRegexMatch match, ref List<ByteRegexMatch> state)
    {
        state.Add(match);
        return true;
    }

    private static byte[][] CreateConcurrentRegexHaystacks()
    {
        return Enumerable.Range(0, ConcurrentHaystackCount)
            .Select(static index => Encoding.UTF8.GetBytes($"package mod{index};\nfunc handler_{index}() {{ return errors.New(\"boom\") }}\n"))
            .ToArray();
    }

    private static byte[] CreateRepeatedBoundedAssignmentInput(int candidateCount)
    {
        return Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("bitbucket repository setting without a credential\n", candidateCount)));
    }

    private static void AssertFunctionMatch(ByteRegexMatch? match, ReadOnlySpan<byte> haystack)
    {
        Assert.True(match.HasValue);
        AssertFunctionMatch(match.Value, haystack);
    }

    private static void AssertFunctionMatch(ByteRegexMatch match, ReadOnlySpan<byte> haystack)
    {
        ReadOnlySpan<byte> value = match.Value(haystack);
        Assert.True(value.StartsWith("func handler_"u8));
        Assert.Equal(haystack.IndexOf("func "u8), match.Start);
    }
}
