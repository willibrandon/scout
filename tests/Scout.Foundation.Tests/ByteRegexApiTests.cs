using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the public byte regex facade intended for package consumers.
/// </summary>
/// <param name="testContext">The context for the current test.</param>
[TestClass]
public sealed class ByteRegexApiTests(TestContext testContext)
{
    private const int BoundedAssignmentSearchTimeoutMilliseconds = 5000;
    private const int RepeatedBoundedAssignmentSearchTimeoutMilliseconds = 5000;
    private const int RepeatedBoundedAssignmentCandidateCount = 800;
    private const int RepeatedBoundedAssignmentStressCandidateCount = 4000;
    private const long RepeatedBoundedAssignmentAllocationLimit = 64 * 1024;
    private const int ConcurrentSearchIterations = 64;
    private const int ConcurrentHaystackCount = 8;
    private const string LazyPrefixAssignmentPattern = "(?i)[a-z]{0,50}?key[a-z]{0,20}=([a-z]{10,20})(?:\"|$)";
    private const string BoundedAssignmentPattern = "(?i)[\\w.-]{0,50}?(?:adafruit)(?:[ \\t\\w.-]{0,20})[\\s'\"]{0,3}(?:=|>|:{1,3}=|\\|\\||:|=>|\\?=|,)[\\x60'\"\\s=]{0,5}([a-z0-9_-]{32})(?:[\\x60'\"\\s;]|\\\\[nr]|$)";
    private const string RepeatedBoundedAssignmentPattern = "(?i)[\\w.-]{0,50}?(?:bitbucket)(?:[ \\t\\w.-]{0,20})[\\s'\"]{0,3}(?:=|>|:{1,3}=|\\|\\||:|=>|\\?=|,)[\\x60'\"\\s=]{0,5}([a-z0-9]{32})(?:[\\x60'\"\\s;]|\\\\[nr]|$)";

    /// <summary>
    /// Verifies byte regex matching exposes byte offsets and spans.
    /// </summary>
    [TestMethod]
    public void FindsFirstMatch()
    {
        var regex = ByteRegex.Compile(@"(?i)[[:alpha:]]+\d+");

        ByteRegexMatch? match = regex.Find("11ABC123 yy"u8);

        Assert.IsTrue(match.HasValue);
        Assert.AreEqual(new ByteRegexMatch(2, 6), match.Value);
        Assert.IsTrue(match.Value.Value("11ABC123 yy"u8).SequenceEqual("ABC123"u8));
        Assert.IsTrue(regex.IsMatch("ABC123"u8));
        Assert.IsFalse(regex.IsMatch("ABC"u8));
    }

    /// <summary>
    /// Verifies capture groups are exposed without leaking automata types.
    /// </summary>
    [TestMethod]
    public void FindsCaptures()
    {
        var regex = ByteRegex.Compile(@"([[:alpha:]]+)(\d+)");

        ByteRegexCaptures? captures = regex.FindCaptures("11ABC123 yy"u8);

        Assert.IsNotNull(captures);
        Assert.AreEqual(3, captures.GroupCount);
        Assert.AreEqual(new ByteRegexMatch(2, 6), captures.Match);
        Assert.AreEqual(new ByteRegexMatch(2, 6), captures.GetGroup(0));
        Assert.AreEqual(new ByteRegexMatch(2, 3), captures.GetGroup(1));
        Assert.AreEqual(new ByteRegexMatch(5, 3), captures.GetGroup(2));
        Assert.AreEqual(3, captures.ParticipatingCount());
    }

    /// <summary>
    /// Verifies a reverse DFA retains the earliest start when a lazy prefix can also accept later.
    /// </summary>
    [TestMethod]
    public void LazyPrefixDfaMatchesPikeVmWholeSpanAndCaptures()
    {
        byte[] input = Encoding.ASCII.GetBytes(
            new string(' ', RegexMetaEngine.UnanchoredLazyDfaHaystackThreshold) +
            "PublicKeyToken=abcdefghijklmnop\"");
        var dfa = ByteRegex.Compile(
            LazyPrefixAssignmentPattern,
            new ByteRegexOptions
            {
                DfaSizeLimit = 16UL * 1024UL * 1024UL,
                EngineMode = ByteRegexEngineMode.General,
                MatchInvalidUtf8 = true,
            });
        var pikeVm = ByteRegex.Compile(
            LazyPrefixAssignmentPattern,
            new ByteRegexOptions
            {
                DfaSizeLimit = 1,
                EngineMode = ByteRegexEngineMode.General,
                MatchInvalidUtf8 = true,
            });
        var expectedMatch = new ByteRegexMatch(4096, 32);
        var expectedCapture = new ByteRegexMatch(4111, 16);

        Assert.AreEqual(expectedMatch, dfa.Find(input));
        Assert.AreEqual(expectedMatch, pikeVm.Find(input));
        ByteRegexCaptures dfaCaptures = Assert.IsExactInstanceOfType<ByteRegexCaptures>(dfa.FindCaptures(input));
        ByteRegexCaptures pikeVmCaptures = Assert.IsExactInstanceOfType<ByteRegexCaptures>(pikeVm.FindCaptures(input));
        Assert.AreEqual(expectedMatch, dfaCaptures.Match);
        Assert.AreEqual(expectedCapture, dfaCaptures.GetGroup(1));
        Assert.AreEqual(pikeVmCaptures.Match, dfaCaptures.Match);
        Assert.AreEqual(pikeVmCaptures.GetGroup(1), dfaCaptures.GetGroup(1));
    }

    /// <summary>
    /// Verifies non-overlapping iteration advances from complete leftmost spans.
    /// </summary>
    [TestMethod]
    public void LazyPrefixDfaIterationReportsCompleteLeftmostSpans()
    {
        byte[] input = Encoding.ASCII.GetBytes(
            new string(' ', RegexMetaEngine.UnanchoredLazyDfaHaystackThreshold) +
            "PublicKeyToken=abcdefghijklmnop\" PublicKeyToken=qrstuvwxyzabcdef\"");
        var regex = ByteRegex.Compile(
            LazyPrefixAssignmentPattern,
            new ByteRegexOptions
            {
                DfaSizeLimit = 16UL * 1024UL * 1024UL,
                EngineMode = ByteRegexEngineMode.General,
                MatchInvalidUtf8 = true,
            });
        var matches = new List<ByteRegexMatch>();

        int count = regex.ForEachMatch(input, ref matches, AddMatch);

        Assert.AreEqual(2, count);
        Assert.AreSequenceEqual<ByteRegexMatch>(
            [new ByteRegexMatch(4096, 32), new ByteRegexMatch(4129, 32)],
            matches);
    }

    /// <summary>
    /// Verifies bounded assignment patterns with Unicode classes do not expand into pathological VM searches.
    /// </summary>
    [TestMethod]
    [Timeout(BoundedAssignmentSearchTimeoutMilliseconds, CooperativeCancellation = true)]
    public void FindsBoundedAssignmentCapturesWithoutStalling()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var regex = ByteRegex.Compile(BoundedAssignmentPattern);
        byte[] positive = Encoding.UTF8.GetBytes("adafruit_api_key = abc123def456ghi789jkl012mno345pq\n");
        byte[] negative = Encoding.UTF8.GetBytes("regex = '''(?i)[\\\\w.-]{0,50}?(?:adafruit)(?:[ \\\\t\\\\w.-]{0,20})''' keywords = [\"adafruit\"]");

        cancellationToken.ThrowIfCancellationRequested();
        ByteRegexMatch? match = regex.Find(positive);
        ByteRegexCaptures? captures = regex.FindCaptures(positive);

        cancellationToken.ThrowIfCancellationRequested();
        Assert.IsTrue(match.HasValue);
        Assert.IsNotNull(captures);
        Assert.AreEqual(match.Value, captures.Match);
        ByteRegexMatch? secret = captures.GetGroup(1);
        Assert.IsTrue(secret.HasValue);
        Assert.IsTrue(secret.Value.Value(positive).SequenceEqual("abc123def456ghi789jkl012mno345pq"u8));
        Assert.IsNull(regex.Find(negative));
        Assert.IsFalse(regex.IsMatch(negative));
        Assert.AreEqual(0, regex.Count(negative));
    }

    /// <summary>
    /// Verifies a large set of repeated nonmatching bounded-assignment candidates is rejected without stalling.
    /// </summary>
    [TestMethod]
    [Timeout(RepeatedBoundedAssignmentSearchTimeoutMilliseconds, CooperativeCancellation = true)]
    public void RejectsManyRepeatedBoundedAssignmentCandidatesWithoutRescanning()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var regex = ByteRegex.Compile(RepeatedBoundedAssignmentPattern);
        byte[] input = CreateRepeatedBoundedAssignmentInput(RepeatedBoundedAssignmentStressCandidateCount);

        cancellationToken.ThrowIfCancellationRequested();
        Assert.IsNull(regex.Find(input));
    }

    /// <summary>
    /// Verifies every public engine mode rejects repeated candidates consistently across search operations.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    [Timeout(RepeatedBoundedAssignmentSearchTimeoutMilliseconds, CooperativeCancellation = true)]
    public void RejectsRepeatedBoundedAssignmentCandidatesAcrossEngineModes(ByteRegexEngineMode engineMode)
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var regex = ByteRegex.Compile(
            RepeatedBoundedAssignmentPattern,
            new ByteRegexOptions { EngineMode = engineMode });
        byte[] terminalMatch = Encoding.UTF8.GetBytes("bitbucket = abc123def456ghi789jkl012mno345pq");
        byte[] input = CreateRepeatedBoundedAssignmentInput(RepeatedBoundedAssignmentCandidateCount);

        cancellationToken.ThrowIfCancellationRequested();
        ByteRegexCaptures? captures = regex.FindCaptures(terminalMatch);
        Assert.IsNotNull(captures);
        Assert.AreEqual(new ByteRegexMatch(0, terminalMatch.Length), captures.Match);
        ByteRegexMatch? secret = captures.GetGroup(1);
        Assert.AreEqual(new ByteRegexMatch(12, 32), secret);
        Assert.IsTrue(secret!.Value.Value(terminalMatch).SequenceEqual("abc123def456ghi789jkl012mno345pq"u8));
        Assert.IsNull(regex.Find(input));
        Assert.IsFalse(regex.IsMatch(input));
        Assert.AreEqual(0, regex.Count(input));
        Assert.IsNull(regex.FindCaptures(input));
    }

    /// <summary>
    /// Verifies warmed repeated-candidate match and capture searches reuse scratch instead of allocating per candidate.
    /// </summary>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    [Timeout(RepeatedBoundedAssignmentSearchTimeoutMilliseconds, CooperativeCancellation = true)]
    public void ReusesScratchForRepeatedBoundedAssignmentSearches(ByteRegexEngineMode engineMode)
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var regex = ByteRegex.Compile(
            RepeatedBoundedAssignmentPattern,
            new ByteRegexOptions { EngineMode = engineMode });
        byte[] input = CreateRepeatedBoundedAssignmentInput(RepeatedBoundedAssignmentCandidateCount);
        Assert.IsNull(regex.Find(input));
        Assert.IsNull(regex.FindCaptures(input));

        cancellationToken.ThrowIfCancellationRequested();
        long findBefore = GC.GetAllocatedBytesForCurrentThread();
        ByteRegexMatch? match = regex.Find(input);
        long findAllocated = GC.GetAllocatedBytesForCurrentThread() - findBefore;

        cancellationToken.ThrowIfCancellationRequested();
        long capturesBefore = GC.GetAllocatedBytesForCurrentThread();
        ByteRegexCaptures? captures = regex.FindCaptures(input);
        long capturesAllocated = GC.GetAllocatedBytesForCurrentThread() - capturesBefore;

        cancellationToken.ThrowIfCancellationRequested();
        Assert.IsNull(match);
        Assert.IsNull(captures);
        Assert.IsInRange(0, RepeatedBoundedAssignmentAllocationLimit, findAllocated);
        Assert.IsInRange(0, RepeatedBoundedAssignmentAllocationLimit, capturesAllocated);
    }

    /// <summary>
    /// Verifies byte mode can search arbitrary non-UTF-8 input.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(new ByteRegexMatch(1, 2), regex.Find(input));
    }

    /// <summary>
    /// Verifies match iteration uses caller-owned state.
    /// </summary>
    [TestMethod]
    public void IteratesMatchesWithState()
    {
        var regex = ByteRegex.Compile(@"\w+");
        var matches = new List<ByteRegexMatch>();

        int count = regex.ForEachMatch(
            "one two 3"u8,
            ref matches,
            AddMatch);

        Assert.AreEqual(3, count);
        Assert.AreSequenceEqual<ByteRegexMatch>([new ByteRegexMatch(0, 3), new ByteRegexMatch(4, 3), new ByteRegexMatch(8, 1)], matches);
    }

    /// <summary>
    /// Verifies syntax errors are surfaced as byte regex parse exceptions.
    /// </summary>
    [TestMethod]
    public void ConvertsSyntaxErrorsToParseException()
    {
        ByteRegexParseException exception = Assert.ThrowsExactly<ByteRegexParseException>(() => ByteRegex.Compile("["));

        Assert.Contains("byte offset", exception.Message, StringComparison.Ordinal);
        Assert.IsNotNull(exception.Offset);
    }

    /// <summary>
    /// Verifies ordered set matching is available through the public facade.
    /// </summary>
    [TestMethod]
    public void FindsSetMatch()
    {
        var set = ByteRegexSet.Compile(["foo[0-9]+", "bar[a-z]+"]);

        ByteRegexSetMatch? match = set.Find("xx barzz foo42"u8);

        Assert.IsTrue(match.HasValue);
        Assert.AreEqual(new ByteRegexSetMatch(1, new ByteRegexMatch(3, 5)), match.Value);
        Assert.IsTrue(set.IsMatch("foo42"u8));
        Assert.AreEqual(2, set.CountMatches("foo1 barz"u8));
    }

    /// <summary>
    /// Verifies a compiled regex can be shared across concurrent lazy-DFA searches.
    /// </summary>
    [TestMethod]
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
                Assert.IsTrue(regex.IsMatch(haystack));

                var matches = new List<ByteRegexMatch>();
                int count = regex.ForEachMatch(haystack, ref matches, AddMatch);

                Assert.AreEqual(1, count);
                Assert.ContainsSingle(matches);
                Assert.AreEqual(match!.Value, matches[0]);
            }
        });
    }

    /// <summary>
    /// Verifies a compiled regex can be shared across concurrent PikeVM searches.
    /// </summary>
    [TestMethod]
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
                Assert.IsTrue(regex.IsMatch(haystack));
                Assert.AreEqual(1, regex.Count(haystack));
            }
        });
    }

    /// <summary>
    /// Verifies the repeated-candidate PikeVM search state can be leased safely by concurrent callers.
    /// </summary>
    [TestMethod]
    [Timeout(RepeatedBoundedAssignmentSearchTimeoutMilliseconds, CooperativeCancellation = true)]
    public void SharedBoundedAssignmentRegexRejectsCandidatesFromMultipleThreads()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var regex = ByteRegex.Compile(
            RepeatedBoundedAssignmentPattern,
            new ByteRegexOptions { EngineMode = ByteRegexEngineMode.AutomataOnly });
        byte[] input = CreateRepeatedBoundedAssignmentInput(16);

        cancellationToken.ThrowIfCancellationRequested();
        Parallel.For(0, ConcurrentSearchIterations, new ParallelOptions { CancellationToken = cancellationToken }, _ =>
        {
            Assert.IsNull(regex.Find(input));
            Assert.IsFalse(regex.IsMatch(input));
        });
    }

    /// <summary>
    /// Verifies generic capture matching can be shared across concurrent searches.
    /// </summary>
    [TestMethod]
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

                Assert.IsNotNull(captures);
                AssertFunctionMatch(captures.Match, haystack);
                ByteRegexMatch? group = captures.GetGroup(1);
                Assert.IsTrue(group.HasValue);
                Assert.IsTrue(group.Value.Value(haystack).StartsWith("handler_"u8));
            }
        });
    }

    /// <summary>
    /// Verifies a compiled regex set can be shared across concurrent searches.
    /// </summary>
    [TestMethod]
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

                Assert.IsTrue(match.HasValue);
                Assert.AreEqual(0, match.Value.PatternId);
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
        Assert.IsTrue(match.HasValue);
        AssertFunctionMatch(match.Value, haystack);
    }

    private static void AssertFunctionMatch(ByteRegexMatch match, ReadOnlySpan<byte> haystack)
    {
        ReadOnlySpan<byte> value = match.Value(haystack);
        Assert.IsTrue(value.StartsWith("func handler_"u8));
        Assert.AreEqual(haystack.IndexOf("func "u8), match.Start);
    }
}
